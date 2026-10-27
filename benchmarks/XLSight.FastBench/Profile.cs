using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace XLSight.FastBench;

/// <summary>
/// Allocation breakdown of one full sheet read, for workbooks outside the frozen manifest
/// (for example a large optional fixture). Each measurement runs in a fresh child process so
/// peak RSS and GC counts belong to that read alone.
/// </summary>
/// <remarks>
/// <c>measure</c> reports elapsed time, cumulative allocation, GC counts and pause share, and peak
/// RSS for the borrowed reader and for safe streaming. <c>attribute</c> walks the same read once
/// more and accounts for the string allocation it implies: cell kinds, shared-string lookups below
/// and above the table's cache bound, how often each uncached entry is reused, and the estimated
/// size of every string the reader materializes. Allocation stacks need an external tracer; see
/// README.md.
/// </remarks>
internal static class Profile
{
    // SharedStringTable caches decoded strings for indexes below this bound only.
    private const int SharedStringCacheEntries = 131072;

    internal static int Run(string path, string sheet, string? outFile)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"error: workbook not found: {path}");
            return 2;
        }

        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture, $"# Allocation profile: {Path.GetFileName(path)} / {sheet}");
        report.AppendLine();
        report.AppendLine(CultureInfo.InvariantCulture, $"Runtime {Environment.Version}, {GCSettings()}. Each row is a fresh process.");
        report.AppendLine();
        report.AppendLine("| mode | rows | cells | elapsed ms | alloc MiB | alloc B/cell | gen0/1/2 | GC pause ms | GC pause % | peak RSS MiB |");
        report.AppendLine("|---|--:|--:|--:|--:|--:|---|--:|--:|--:|");
        foreach (string mode in (string[])["reader", "stream"])
        {
            string? line = RunChild(path, sheet, mode, "measure");
            if (line is null)
            {
                return 3;
            }

            report.AppendLine(line);
        }

        report.AppendLine();
        string? attribution = RunChild(path, sheet, "reader", "attribute");
        if (attribution is null)
        {
            return 3;
        }

        report.Append(attribution);
        Console.Write(report.ToString());
        if (outFile is not null)
        {
            File.WriteAllText(outFile, report.ToString());
            Console.Error.WriteLine($"wrote {outFile}");
        }

        return 0;
    }

    internal static int RunChildCommand(string path, string sheet, string mode, string what)
    {
        Console.Write(what == "measure" ? Measure(path, sheet, mode) : Attribute(path, sheet));
        return 0;
    }

    private static string GCSettings() =>
        (System.Runtime.GCSettings.IsServerGC ? "server" : "workstation") + " GC, latency "
        + System.Runtime.GCSettings.LatencyMode;

    private static string? RunChild(string path, string sheet, string mode, string what)
    {
        string self = typeof(Profile).Assembly.Location;
        string host = Environment.ProcessPath ?? "dotnet";
        var psi = new ProcessStartInfo(host)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (!Path.GetFileNameWithoutExtension(host).Equals(Path.GetFileNameWithoutExtension(self), StringComparison.Ordinal))
        {
            psi.ArgumentList.Add(self);
        }

        foreach (string arg in (string[])["profile-child", "--path", path, "--sheet", sheet, "--mode", mode, "--what", what])
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            Console.Error.WriteLine($"error: profile child ({mode}, {what}) exited {process.ExitCode}: {stderr.Result}");
            return null;
        }

        return stdout.Result.TrimEnd('\n');
    }

    private static string Measure(string path, string sheet, string mode)
    {
        int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
        TimeSpan pause0 = GC.GetTotalPauseDuration();
        long alloc0 = GC.GetTotalAllocatedBytes(precise: true);
        var sw = Stopwatch.StartNew();
        (long rows, long cells) = Read(path, sheet, mode, onCell: null);
        sw.Stop();
        long alloc = GC.GetTotalAllocatedBytes(precise: true) - alloc0;
        double pauseMs = (GC.GetTotalPauseDuration() - pause0).TotalMilliseconds;
        double elapsedMs = sw.Elapsed.TotalMilliseconds;
        return string.Create(CultureInfo.InvariantCulture,
            $"| {mode} | {rows} | {cells} | {elapsedMs:F0} | {alloc / 1048576.0:F1} | {(cells == 0 ? 0 : alloc / (double)cells):F1} | "
            + $"{GC.CollectionCount(0) - g0}/{GC.CollectionCount(1) - g1}/{GC.CollectionCount(2) - g2} | {pauseMs:F0} | "
            + $"{100 * pauseMs / elapsedMs:F1} | {Runner.ReadPeakRss() / 1048576.0:F1} |\n");
    }

    private static string Attribute(string path, string sheet)
    {
        var kinds = new Dictionary<CellType, long>();
        var cachedIds = new HashSet<int>();
        var uncachedHits = new Dictionary<int, int>();
        long cachedAccesses = 0, uncachedAccesses = 0;
        long cachedFirstTouchBytes = 0, uncachedBytes = 0, otherTextCells = 0, otherTextBytes = 0;

        long alloc0 = GC.GetTotalAllocatedBytes(precise: true);
        Read(path, sheet, "reader", (in ExcelCellValue cell) =>
        {
            kinds[cell.CellType] = kinds.GetValueOrDefault(cell.CellType) + 1;
            if (cell.TryGetSharedStringId(out int id))
            {
                if (id < SharedStringCacheEntries)
                {
                    cachedAccesses++;
                    if (cachedIds.Add(id))
                    {
                        cachedFirstTouchBytes += StringBytes(cell.AsText().Length);
                    }
                }
                else
                {
                    uncachedAccesses++;
                    uncachedHits[id] = uncachedHits.GetValueOrDefault(id) + 1;
                    uncachedBytes += StringBytes(cell.AsText().Length);
                }
            }
            else if (cell.CellType is CellType.Text or CellType.Formula or CellType.Error)
            {
                string text = cell.CellType switch
                {
                    CellType.Text => cell.AsText(),
                    CellType.Formula => cell.AsFormula(),
                    _ => cell.AsError(),
                };
                otherTextCells++;
                otherTextBytes += StringBytes(text.Length);
            }
        });
        long allocWithAccounting = GC.GetTotalAllocatedBytes(precise: true) - alloc0;

        var sb = new StringBuilder();
        sb.AppendLine("## Cells");
        sb.AppendLine();
        sb.AppendLine(string.Join(", ", kinds.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value}")));
        sb.AppendLine();
        sb.AppendLine("## Strings the borrowed reader materializes (estimated object sizes, 64-bit)");
        sb.AppendLine();
        sb.AppendLine("| source | accesses | distinct | est. MiB |");
        sb.AppendLine("|---|--:|--:|--:|");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| shared strings, index < {SharedStringCacheEntries} (decoded once, cached) | {cachedAccesses} | {cachedIds.Count} | {cachedFirstTouchBytes / 1048576.0:F1} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| shared strings, index ≥ {SharedStringCacheEntries} (decoded on every access) | {uncachedAccesses} | {uncachedHits.Count} | {uncachedBytes / 1048576.0:F1} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| inline strings, formula strings, errors | {otherTextCells} | — | {otherTextBytes / 1048576.0:F1} |");
        long repeats = uncachedAccesses - uncachedHits.Count;
        double repeatMiB = uncachedAccesses == 0 ? 0 : uncachedBytes * (double)repeats / uncachedAccesses / 1048576.0;
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Repeat decodes of uncached entries: {repeats} accesses, ~{repeatMiB:F1} MiB at the mean uncached size. This is the most any cache over the uncached range could save.");
        sb.AppendLine();
        sb.AppendLine("Uncached reuse (accesses per entry → entries / accesses): " + string.Join(", ",
            uncachedHits.Values
                .GroupBy(static v => v switch { 1 => "1", 2 => "2", <= 4 => "3–4", <= 16 => "5–16", _ => ">16" })
                .OrderBy(static g => g.Min())
                .Select(static g => $"{g.Key}: {g.Count()} / {g.Sum()}")));
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Allocation of this accounting pass (includes its own dictionaries): {allocWithAccounting / 1048576.0:F1} MiB.");
        sb.AppendLine("Compare the reader row above against the string total: the remainder is the shared-string table itself (UTF-8 arena and index) plus anything else.");
        return sb.ToString();
    }

    private delegate void CellVisitor(in ExcelCellValue cell);

    private static (long Rows, long Cells) Read(string path, string sheet, string mode, CellVisitor? onCell)
    {
        long rows = 0, cells = 0;
        using var workbook = ExcelWorkbook.Open(path);
        if (mode == "stream")
        {
            foreach (ExcelRow row in workbook.StreamSheet(sheet))
            {
                rows++;
                foreach (ExcelCellValue cell in row.Cells)
                {
                    cells++;
                    onCell?.Invoke(cell);
                }
            }
        }
        else
        {
            using ExcelSheetReader reader = workbook.GetSheetReader(sheet);
            while (reader.Read())
            {
                rows++;
                foreach (ExcelCellValue cell in reader.Current.Cells)
                {
                    cells++;
                    onCell?.Invoke(cell);
                }
            }
        }

        return (rows, cells);
    }

    // 64-bit string: 8-byte header + method table + 4-byte length + UTF-16 chars + terminator, 8-byte aligned.
    private static long StringBytes(int chars) => (22 + (2L * chars) + 7) & ~7L;
}
