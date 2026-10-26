using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace XLSight.FastBench;

internal sealed class RunOptions
{
    public required string Root { get; init; }
    public required string ManifestPath { get; init; }
    public required string OutDir { get; init; }
    public string? Filter { get; init; }
    public string? Label { get; init; }
    public string? GitRevision { get; init; }
    public string? GitDirty { get; init; }
    public int? SuiteBudgetOverrideMs { get; init; }
    public bool InternalWatchdog { get; init; } = true;
    public bool KeepSamples { get; init; } = true;
}

/// <summary>
/// Runs the selected manifest cases against a monotonic suite deadline. The deadline is measured
/// from process start (startup is estimated once and charged to the budget), every case gets a
/// bounded slice, and a timer thread enforces a hard deadline even if an operation never returns.
/// </summary>
internal sealed class Runner
{
    public const int ExitOk = 0;
    public const int ExitValidationFailed = 2;
    public const int ExitIncomplete = 3;
    public const int ExitHardDeadline = 4;

    private readonly RunOptions _options;
    private readonly Manifest _manifest;
    private readonly string _manifestSha;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly double _startupMs;
    private readonly Lock _gate = new();
    private RunResult _result = null!;
    private bool _finished;

    public Runner(RunOptions options)
    {
        _startupMs = EstimateStartupMs();
        _options = options;
        (_manifest, _manifestSha) = Manifest.Load(options.ManifestPath);
    }

    private int SuiteBudgetMs => _options.SuiteBudgetOverrideMs ?? _manifest.SuiteBudgetMs;

    private int HardDeadlineMs => _options.SuiteBudgetOverrideMs is { } budget
        ? budget + (_manifest.HardDeadlineMs - _manifest.SuiteBudgetMs)
        : _manifest.HardDeadlineMs;

    /// <summary>Milliseconds since process start, monotonic after the one-time startup estimate.</summary>
    private double Now => _startupMs + _clock.Elapsed.TotalMilliseconds;

    private double Remaining => SuiteBudgetMs - Now;

    public int Run()
    {
        _result = new RunResult
        {
            StartedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            Label = _options.Label,
            Git = new GitIdentity { Revision = _options.GitRevision, Dirty = _options.GitDirty },
            Build = DescribeBuild(),
            Runtime = DescribeRuntime(),
            Manifest = new ManifestIdentity
            {
                Path = Path.GetRelativePath(_options.Root, _options.ManifestPath),
                Version = _manifest.Version,
                Sha256 = _manifestSha,
                Filter = _options.Filter,
            },
            Policy = new SamplingPolicy
            {
                SuiteBudgetMs = SuiteBudgetMs,
                ReserveMs = _manifest.ReserveMs,
                HardDeadlineMs = HardDeadlineMs,
            },
        };
        _result.Timing.StartupMs = Math.Round(_startupMs, 1);

        using Timer? watchdog = _options.InternalWatchdog
            ? new Timer(_ => HardDeadline(), null, TimeSpan.FromMilliseconds(Math.Max(0, HardDeadlineMs - Now)), Timeout.InfiniteTimeSpan)
            : null;

        Dictionary<string, string> fixtures = VerifyFixtures();
        List<CaseDef> cases = SelectCases();
        if (cases.Count == 0)
        {
            _result.Problems.Add($"No cases match filter '{_options.Filter}'.");
        }

        foreach (CaseDef def in cases)
        {
            var caseResult = new CaseResult
            {
                Id = def.Id,
                Tags = def.Tags,
                Isolated = def.Isolated,
                Unit = def.Unit,
                OpsPerSample = def.OpsPerSample,
            };
            lock (_gate)
            {
                _result.Cases.Add(caseResult);
            }

            double caseStart = Now;
            try
            {
                if (def.Fixture is not null && !fixtures.ContainsKey(def.Fixture))
                {
                    caseResult.Status = "failed";
                    caseResult.Message = $"fixture '{def.Fixture}' missing or hash mismatch";
                    continue;
                }

                double budget = Math.Min(def.BudgetMs, Remaining - _manifest.ReserveMs);
                if (budget < 50)
                {
                    caseResult.Status = "skipped";
                    caseResult.Message = "suite deadline reached before case start";
                    _result.Timing.DeadlineHit = true;
                    continue;
                }

                string? fixturePath = def.Fixture is null ? null : fixtures[def.Fixture];
                if (def.Isolated)
                {
                    RunIsolated(def, caseResult, budget);
                }
                else
                {
                    RunInProcess(def, fixturePath, caseResult, budget);
                }
            }
            catch (Exception ex)
            {
                caseResult.Status = "failed";
                caseResult.Message = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                caseResult.CaseElapsedMs = Math.Round(Now - caseStart, 1);
            }
        }

        return Finish(hardDeadline: false);
    }

    private Dictionary<string, string> VerifyFixtures()
    {
        var available = new Dictionary<string, string>(StringComparer.Ordinal);
        HashSet<string> needed = [.. SelectCases().Where(c => c.Fixture is not null).Select(c => c.Fixture!)];
        foreach (FixtureDef fixture in _manifest.Fixtures.Where(f => needed.Contains(f.Name)))
        {
            string path = Path.Combine(_options.Root, fixture.Path);
            if (!File.Exists(path))
            {
                _result.Fixtures.Add(new FixtureResult { Name = fixture.Name, Path = fixture.Path, Status = "missing" });
                _result.Problems.Add(fixture.Generator is null
                    ? $"Fixture '{fixture.Name}' not found at {fixture.Path}."
                    : $"Fixture '{fixture.Name}' not generated; run `fastbench.sh fixtures`.");
                continue;
            }

            string sha = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
            bool ok = fixture.Sha256 is null || string.Equals(sha, fixture.Sha256, StringComparison.Ordinal);
            _result.Fixtures.Add(new FixtureResult
            {
                Name = fixture.Name,
                Path = fixture.Path,
                Sha256 = sha,
                Bytes = new FileInfo(path).Length,
                Status = fixture.Sha256 is null ? "unpinned" : ok ? "ok" : "hash-mismatch",
            });
            if (!ok)
            {
                _result.Problems.Add($"Fixture '{fixture.Name}' hash {sha} does not match manifest {fixture.Sha256}; regenerate or re-baseline with a new manifest version.");
                continue;
            }

            if (fixture.Sha256 is null)
            {
                _result.Problems.Add($"Fixture '{fixture.Name}' has no pinned hash in the manifest.");
            }

            available[fixture.Name] = path;
        }

        return available;
    }

    private List<CaseDef> SelectCases()
    {
        if (_options.Filter is null)
        {
            return [.. _manifest.Cases.Where(c => c.Default)];
        }

        var regex = new Regex(_options.Filter, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return [.. _manifest.Cases.Where(c => regex.IsMatch(c.Id) || (c.Tags?.Any(regex.IsMatch) ?? false))];
    }

    private void RunInProcess(CaseDef def, string? fixturePath, CaseResult result, double budgetMs)
    {
        double caseStart = Now;
        long fixtureBytes = fixturePath is null ? 0 : new FileInfo(fixturePath).Length;

        // The first op of the case is the untimed deep validation (full text content folded into the
        // digest); it doubles as JIT warmup. Its duration is reported as firstOpMs for reference only.
        long t0 = Stopwatch.GetTimestamp();
        OpResult deep = Operations.Run(def, fixturePath, deep: true);
        result.FirstOpMs = Math.Round(Stopwatch.GetElapsedTime(t0).TotalMilliseconds, 3);
        result.WarmupOps = 1;
        result.DeepDigest = Digest.Hex(deep.Digest);
        if (def.Expected is not null && !string.Equals(result.DeepDigest, def.Expected.DeepDigest, StringComparison.Ordinal))
        {
            Fail(result, $"deep digest {result.DeepDigest} != expected {def.Expected.DeepDigest}");
            return;
        }

        OpResult first = deep;
        while (Now - caseStart < def.WarmupMs && Now - caseStart < budgetMs / 3)
        {
            first = Operations.Run(def, fixturePath, deep: false);
            result.WarmupOps++;
            if (!CheckCheap(def, first, result))
            {
                return;
            }
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var samples = new List<double>(def.MaxSamples);
        long allocated = 0;
        int gen0 = 0, gen1 = 0, gen2 = 0;
        while (samples.Count < def.MaxSamples
            && (Now - caseStart < budgetMs || samples.Count < def.MinSamples)
            && Remaining > _manifest.ReserveMs)
        {
            int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
            long a0 = GC.GetTotalAllocatedBytes(precise: true);
            long start = Stopwatch.GetTimestamp();
            ulong combined = 0;
            OpResult last = default;
            for (int i = 0; i < def.OpsPerSample; i++)
            {
                last = Operations.Run(def, fixturePath, deep: false);
                combined ^= last.Digest;
            }

            double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            allocated += GC.GetTotalAllocatedBytes(precise: true) - a0;
            gen0 += GC.CollectionCount(0) - g0;
            gen1 += GC.CollectionCount(1) - g1;
            gen2 += GC.CollectionCount(2) - g2;
            samples.Add(elapsed / def.OpsPerSample);
            if (!CheckCheap(def, last, result) || (def.OpsPerSample % 2 == 1 ? combined != last.Digest : combined != 0))
            {
                if (result.Status != "failed")
                {
                    Fail(result, "digest varied between ops within a sample");
                }

                return;
            }
        }

        long ops = (long)samples.Count * def.OpsPerSample;
        result.Samples = samples.Count;
        result.MedianMs = Math.Round(Stats.Median(samples), 4);
        result.MadMs = Math.Round(Stats.Mad(samples), 4);
        result.MinMs = samples.Count == 0 ? double.NaN : Math.Round(samples.Min(), 4);
        result.MaxMs = samples.Count == 0 ? double.NaN : Math.Round(samples.Max(), 4);
        result.UnitsPerOp = first.Units;
        result.UnitsPerSecond = Math.Round(first.Units / (result.MedianMs / 1000), 1);
        result.FixtureMiBPerSecond = Math.Round(fixtureBytes / 1048576.0 / (result.MedianMs / 1000), 2);
        result.AllocatedBytesPerOp = ops == 0 ? double.NaN : Math.Round((double)allocated / ops, 1);
        result.Gen0PerOp = ops == 0 ? double.NaN : Math.Round((double)gen0 / ops, 3);
        result.Gen1PerOp = ops == 0 ? double.NaN : Math.Round((double)gen1 / ops, 3);
        result.Gen2PerOp = ops == 0 ? double.NaN : Math.Round((double)gen2 / ops, 3);
        if (_options.KeepSamples)
        {
            result.SampleMs = [.. samples.Select(s => Math.Round(s, 4))];
        }

        if (samples.Count < def.MinSamples)
        {
            result.Status = "incomplete";
            result.Message = $"suite deadline left {samples.Count}/{def.MinSamples} samples";
            _result.Timing.DeadlineHit = true;
            return;
        }

        result.Status = result.Validation == "ok" ? "ok" : "unverified";
    }

    private bool CheckCheap(CaseDef def, OpResult op, CaseResult result)
    {
        string digest = Digest.Hex(op.Digest);
        if (result.Digest is null)
        {
            result.Digest = digest;
            if (def.Expected is null)
            {
                result.Validation = "no-expectation";
                return true;
            }

            if (!string.Equals(digest, def.Expected.Digest, StringComparison.Ordinal) || op.Units != def.Expected.Units)
            {
                Fail(result, $"digest {digest}/units {op.Units} != expected {def.Expected.Digest}/{def.Expected.Units}");
                return false;
            }

            result.Validation = "ok";
            return true;
        }

        if (!string.Equals(digest, result.Digest, StringComparison.Ordinal))
        {
            Fail(result, $"digest changed between ops: {digest} vs {result.Digest}");
            return false;
        }

        return true;
    }

    private static void Fail(CaseResult result, string message)
    {
        result.Status = "failed";
        result.Validation = "mismatch";
        result.Message = message;
    }

    private void RunIsolated(CaseDef def, CaseResult result, double budgetMs)
    {
        string host = Environment.ProcessPath ?? "dotnet";
        string self = Assembly.GetEntryAssembly()!.Location;
        var psi = new ProcessStartInfo(host)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (!host.EndsWith(Path.GetFileNameWithoutExtension(self), StringComparison.Ordinal)
            && !host.EndsWith(Path.GetFileNameWithoutExtension(self) + ".exe", StringComparison.Ordinal))
        {
            psi.ArgumentList.Add(self);
        }

        foreach (string arg in (string[])["child", "--root", _options.Root, "--manifest", _options.ManifestPath, "--case", def.Id])
        {
            psi.ArgumentList.Add(arg);
        }

        long start = Stopwatch.GetTimestamp();
        using Process child = Process.Start(psi)!;
        Task<string> stdout = child.StandardOutput.ReadToEndAsync();
        Task<string> stderr = child.StandardError.ReadToEndAsync();
        int waitMs = (int)Math.Max(0, Math.Min(budgetMs, Remaining - _manifest.ReserveMs));
        if (!child.WaitForExit(waitMs))
        {
            child.Kill(entireProcessTree: true);
            child.WaitForExit();
            result.Status = "incomplete";
            result.Message = $"isolated child exceeded {waitMs} ms and was killed";
            _result.Timing.DeadlineHit = true;
            return;
        }

        double wall = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        if (child.ExitCode != 0)
        {
            Fail(result, $"child exited {child.ExitCode}: {stderr.Result.Trim()}");
            return;
        }

        ChildReport report = JsonSerializer.Deserialize<ChildReport>(stdout.Result, Manifest.JsonOptions)
            ?? throw new InvalidDataException("child produced no report");
        report.Metrics.ChildWallMs = Math.Round(wall, 1);
        result.Isolation = report.Metrics;
        result.Digest = report.Digest;
        result.DeepDigest = report.DeepDigest;
        result.UnitsPerOp = report.Units;
        result.Samples = 1;
        result.FirstOpMs = report.Metrics.ColdOpMs;
        result.MedianMs = report.Metrics.WarmOpMs;
        result.AllocatedBytesPerOp = report.Metrics.ColdOpAllocatedBytes;
        if (def.Expected is null)
        {
            result.Validation = "no-expectation";
            result.Status = "unverified";
        }
        else if (report.Digest != def.Expected.Digest || report.DeepDigest != def.Expected.DeepDigest || report.Units != def.Expected.Units)
        {
            Fail(result, $"child digest {report.Digest}/{report.DeepDigest}/{report.Units} != expected");
        }
        else
        {
            result.Validation = "ok";
            result.Status = "ok";
        }
    }

    /// <summary>Entry point of an isolated child: one cold op, one warm op, one deep validation op.</summary>
    public static int RunChild(string root, string manifestPath, string caseId)
    {
        (Manifest manifest, _) = Manifest.Load(manifestPath);
        CaseDef def = manifest.Cases.Single(c => c.Id == caseId);
        string? path = def.Fixture is null ? null : Path.Combine(root, manifest.Fixtures.Single(f => f.Name == def.Fixture).Path);
        long baselineHwm = ReadPeakRss();

        int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
        long a0 = GC.GetTotalAllocatedBytes(precise: true);
        long t0 = Stopwatch.GetTimestamp();
        OpResult cold = Operations.Run(def, path, deep: false);
        double coldMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - a0;
        var metrics = new IsolatedMetrics
        {
            ColdOpMs = Math.Round(coldMs, 3),
            ColdOpAllocatedBytes = allocated,
            Gen0 = GC.CollectionCount(0) - g0,
            Gen1 = GC.CollectionCount(1) - g1,
            Gen2 = GC.CollectionCount(2) - g2,
            BaselineHwmBytes = baselineHwm,
            PeakRssBytes = ReadPeakRss(),
            RetainedManagedBytesAfterOp = GC.GetTotalMemory(forceFullCollection: true),
        };

        long t1 = Stopwatch.GetTimestamp();
        OpResult warm = Operations.Run(def, path, deep: false);
        metrics.WarmOpMs = Math.Round(Stopwatch.GetElapsedTime(t1).TotalMilliseconds, 3);
        OpResult deep = Operations.Run(def, path, deep: true);
        if (warm.Digest != cold.Digest)
        {
            Console.Error.WriteLine("digest changed between cold and warm op");
            return ExitValidationFailed;
        }

        var report = new ChildReport
        {
            Digest = Digest.Hex(cold.Digest),
            DeepDigest = Digest.Hex(deep.Digest),
            Units = cold.Units,
            Metrics = metrics,
        };
        Console.Out.Write(JsonSerializer.Serialize(report, Manifest.JsonOptions));
        return ExitOk;
    }

    private sealed class ChildReport
    {
        public required string Digest { get; init; }
        public required string DeepDigest { get; init; }
        public long Units { get; init; }
        public required IsolatedMetrics Metrics { get; init; }
    }

    /// <summary>Process RSS high-water mark (VmHWM) on Linux, peak working set elsewhere.</summary>
    internal static long ReadPeakRss()
    {
        if (OperatingSystem.IsLinux())
        {
            foreach (string line in File.ReadLines("/proc/self/status"))
            {
                if (line.StartsWith("VmHWM:", StringComparison.Ordinal))
                {
                    string kb = line["VmHWM:".Length..].Trim().Split(' ')[0];
                    return long.Parse(kb, CultureInfo.InvariantCulture) * 1024;
                }
            }
        }

        using var process = Process.GetCurrentProcess();
        return process.PeakWorkingSet64;
    }

    private void HardDeadline()
    {
        lock (_gate)
        {
            if (_finished)
            {
                return;
            }

            foreach (CaseResult c in _result.Cases.Where(c => c.Status is "pending" or "running"))
            {
                c.Status = "incomplete";
                c.Message = "hard deadline: operation still running";
            }

            _result.Problems.Add($"Hard deadline of {HardDeadlineMs} ms reached; process terminated by internal watchdog.");
        }

        Environment.Exit(Finish(hardDeadline: true));
    }

    private int Finish(bool hardDeadline)
    {
        lock (_gate)
        {
            if (_finished && !hardDeadline)
            {
                return _result.ExitCode;
            }

            _finished = true;
            _result.Timing.SuiteElapsedMs = Math.Round(Now, 1);
            List<string> statuses = [.. _result.Cases.Select(c => c.Status)];
            bool failed = statuses.Contains("failed") || _result.Fixtures.Any(f => f.Status is "hash-mismatch" or "missing");
            bool incomplete = hardDeadline || statuses.Any(s => s is "incomplete" or "skipped" or "pending" or "unverified")
                || _result.Problems.Count > 0 || _result.Cases.Count == 0;
            if (_result.Timing.SuiteElapsedMs > SuiteBudgetMs)
            {
                incomplete = true;
                _result.Problems.Add($"Suite took {_result.Timing.SuiteElapsedMs} ms, over its {SuiteBudgetMs} ms budget.");
            }

            (_result.Status, _result.ExitCode) = hardDeadline ? ("incomplete", ExitHardDeadline)
                : failed ? ("failed", ExitValidationFailed)
                : incomplete ? ("incomplete", ExitIncomplete)
                : ("complete", ExitOk);

            Directory.CreateDirectory(_options.OutDir);
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string name = _options.Label is null ? stamp : $"{stamp}-{_options.Label}";
            string json = JsonSerializer.Serialize(_result, Manifest.JsonOptions);
            string jsonPath = Path.Combine(_options.OutDir, name + ".json");
            File.WriteAllText(jsonPath, json);
            File.WriteAllText(Path.Combine(_options.OutDir, "latest.json"), json);
            string table = Report.Table(_result);
            File.WriteAllText(Path.Combine(_options.OutDir, name + ".md"), table);
            Console.Out.Write(table);
            Console.Out.WriteLine($"status: {_result.Status} (exit {_result.ExitCode}); suite {_result.Timing.SuiteElapsedMs:F0} ms of {SuiteBudgetMs} ms budget; results: {Path.GetRelativePath(_options.Root, jsonPath)}");
            foreach (string problem in _result.Problems)
            {
                Console.Out.WriteLine($"problem: {problem}");
            }

            Console.Out.Flush();
            return _result.ExitCode;
        }
    }

    private static double EstimateStartupMs()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            return Math.Max(0, (DateTime.Now - process.StartTime).TotalMilliseconds);
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }

    private static BuildIdentity DescribeBuild()
    {
        var assemblies = new Dictionary<string, AssemblyIdentity>(StringComparer.Ordinal);
        foreach (string name in (string[])["XLSight", "XLSight.Query", "XLSight.Layout", "XLSight.FastBench"])
        {
            string path = Path.Combine(AppContext.BaseDirectory, name + ".dll");
            if (!File.Exists(path))
            {
                continue;
            }

            Assembly? loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name);
            assemblies[name] = new AssemblyIdentity
            {
                Sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))),
                InformationalVersion = (loaded ?? Assembly.LoadFrom(path)).GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                LastWriteUtc = File.GetLastWriteTimeUtc(path).ToString("O", CultureInfo.InvariantCulture),
            };
        }

        string configuration = typeof(Runner).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "unknown";
        return new BuildIdentity { Configuration = configuration, Assemblies = assemblies };
    }

    private static RuntimeIdentity DescribeRuntime()
    {
        string? cpu = null;
        if (OperatingSystem.IsLinux() && File.Exists("/proc/cpuinfo"))
        {
            cpu = File.ReadLines("/proc/cpuinfo").FirstOrDefault(l => l.StartsWith("model name", StringComparison.Ordinal))?.Split(':', 2)[1].Trim();
        }

        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            string key = (string)entry.Key;
            if (key.StartsWith("DOTNET_", StringComparison.Ordinal) && !key.Contains("TOKEN", StringComparison.OrdinalIgnoreCase))
            {
                env[key] = (string?)entry.Value ?? "";
            }
        }

        var gc = new StringBuilder();
        gc.Append(GCSettings.IsServerGC ? "server" : "workstation");
        gc.Append(AppContext.TryGetSwitch("System.GC.Concurrent", out bool concurrent) && !concurrent ? ", non-concurrent" : ", concurrent");
        gc.Append(CultureInfo.InvariantCulture, $", latency={GCSettings.LatencyMode}");
        gc.Append(CultureInfo.InvariantCulture, $", heapHardLimit={GC.GetGCMemoryInfo().TotalAvailableMemoryBytes}");
        return new RuntimeIdentity
        {
            Framework = RuntimeInformation.FrameworkDescription,
            Os = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            ProcessorCount = Environment.ProcessorCount,
            CpuModel = cpu,
            Gc = gc.ToString(),
            DotnetEnvironment = env,
        };
    }
}
