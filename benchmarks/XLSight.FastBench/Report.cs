using System.Globalization;
using System.Text;
using System.Text.Json;

namespace XLSight.FastBench;

internal static class Report
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Table(RunResult run)
    {
        var sb = new StringBuilder();
        sb.Append(Inv, $"# FastBench {run.Label ?? ""} — {run.Status}\n\n");
        sb.Append(Inv, $"rev `{run.Git.Revision ?? "?"}` dirty `{run.Git.Dirty ?? "?"}` · XLSight.dll `{Short(run.Build.Assemblies.GetValueOrDefault("XLSight")?.Sha256)}` · manifest {run.Manifest.Version} `{Short(run.Manifest.Sha256)}` · {run.Runtime.Framework} · {run.Runtime.ProcessorCount} CPU · {run.Runtime.Gc}\n");
        sb.Append(Inv, $"suite {run.Timing.SuiteElapsedMs:F0} ms (startup {run.Timing.StartupMs:F0} ms) of {run.Policy.SuiteBudgetMs} ms budget\n\n");
        sb.Append("Warm steady state (in-process, after warmup):\n\n");
        sb.Append("| case | status | n | median ms | MAD % | first-op ms | throughput | alloc/op | gen0/op |\n");
        sb.Append("|---|---|--:|--:|--:|--:|--:|--:|--:|\n");
        foreach (CaseResult c in run.Cases.Where(c => !c.Isolated))
        {
            sb.Append(Inv, $"| {c.Id} | {c.Status} | {c.Samples} | {c.MedianMs:F3} | {RelMad(c):F1} | {c.FirstOpMs:F1} | {Throughput(c)} | {Bytes(c.AllocatedBytesPerOp)} | {c.Gen0PerOp:F2} |\n");
        }

        var isolated = run.Cases.Where(c => c.Isolated).ToList();
        if (isolated.Count > 0)
        {
            sb.Append("\nCold, fresh process (one op; peak RSS = process VmHWM incl. runtime):\n\n");
            sb.Append("| case | status | cold op ms | warm op ms | alloc (cold op) | baseline RSS | peak RSS | retained managed | child wall ms |\n");
            sb.Append("|---|---|--:|--:|--:|--:|--:|--:|--:|\n");
            foreach (CaseResult c in isolated)
            {
                IsolatedMetrics? m = c.Isolation;
                sb.Append(Inv, $"| {c.Id} | {c.Status} | {m?.ColdOpMs:F1} | {m?.WarmOpMs:F1} | {Bytes(m?.ColdOpAllocatedBytes)} | {Bytes(m?.BaselineHwmBytes)} | {Bytes(m?.PeakRssBytes)} | {Bytes(m?.RetainedManagedBytesAfterOp)} | {m?.ChildWallMs:F0} |\n");
            }
        }

        foreach (CaseResult c in run.Cases.Where(c => c.Message is not null))
        {
            sb.Append(Inv, $"\n- {c.Id}: {c.Message}");
        }

        return sb.Append('\n').ToString();
    }

    private static double RelMad(CaseResult c) => c.MedianMs > 0 ? 100 * c.MadMs / c.MedianMs : double.NaN;

    private static string Throughput(CaseResult c) => c.UnitsPerSecond switch
    {
        >= 1e6 => string.Create(Inv, $"{c.UnitsPerSecond / 1e6:F2} M {c.Unit}/s"),
        >= 1e3 => string.Create(Inv, $"{c.UnitsPerSecond / 1e3:F1} K {c.Unit}/s"),
        _ => string.Create(Inv, $"{c.UnitsPerSecond:F1} {c.Unit}/s"),
    };

    private static string Short(string? sha) => sha is null ? "?" : sha[..Math.Min(12, sha.Length)];

    private static string Bytes(double? bytes) => bytes switch
    {
        null => "-",
        double b when double.IsNaN(b) => "-",
        >= 1 << 20 => string.Create(Inv, $"{bytes / 1048576.0:F1} MiB"),
        >= 1 << 10 => string.Create(Inv, $"{bytes / 1024.0:F1} KiB"),
        _ => string.Create(Inv, $"{bytes:F0} B"),
    };

    /// <summary>
    /// Compares runs of the same manifest. With several files per side, each case uses the median
    /// of per-run medians, and noise is the larger of the per-side spread and the within-run MAD.
    /// </summary>
    public static int Compare(IReadOnlyList<string> basePaths, IReadOnlyList<string> candidatePaths, double minEffectPercent)
    {
        List<RunResult> baseRuns = [.. basePaths.Select(Load)];
        List<RunResult> candRuns = [.. candidatePaths.Select(Load)];
        HashSet<string> manifests = [.. baseRuns.Concat(candRuns).Select(r => r.Manifest.Sha256)];
        if (manifests.Count != 1)
        {
            Console.Error.WriteLine("Runs use different manifests; measure a new baseline with the candidate's manifest.");
            return 2;
        }

        HashSet<string> fixtureSets = [.. baseRuns.Concat(candRuns).Select(r => string.Join(',', r.Fixtures.OrderBy(f => f.Name).Select(f => f.Sha256)))];
        if (fixtureSets.Count != 1)
        {
            Console.Error.WriteLine("Runs used different fixture bytes; regenerate fixtures and re-measure both sides.");
            return 2;
        }

        foreach (RunResult run in baseRuns.Concat(candRuns).Where(r => r.Status != "complete"))
        {
            Console.Error.WriteLine($"warning: run {run.StartedUtc} ({run.Label}) is {run.Status}; its cases are compared only where complete.");
        }

        var sb = new StringBuilder();
        sb.Append(Inv, $"base: {Describe(baseRuns)}\ncand: {Describe(candRuns)}\n\n");
        sb.Append("| case | base ms | cand ms | Δ time | noise ± | verdict | base alloc/op | cand alloc/op | Δ alloc |\n");
        sb.Append("|---|--:|--:|--:|--:|---|--:|--:|--:|\n");
        IEnumerable<string> ids = baseRuns[0].Cases.Select(c => c.Id).Union(candRuns[0].Cases.Select(c => c.Id));
        foreach (string id in ids)
        {
            List<CaseResult> b = [.. baseRuns.Select(r => r.Cases.FirstOrDefault(c => c.Id == id)).OfType<CaseResult>().Where(c => c.Status == "ok")];
            List<CaseResult> k = [.. candRuns.Select(r => r.Cases.FirstOrDefault(c => c.Id == id)).OfType<CaseResult>().Where(c => c.Status == "ok")];
            if (b.Count == 0 || k.Count == 0)
            {
                sb.Append(Inv, $"| {id} | {(b.Count == 0 ? "n/a" : "")} | {(k.Count == 0 ? "n/a" : "")} | | | missing | | | |\n");
                continue;
            }

            bool isolated = b[0].Isolated;
            double bm = Stats.Median([.. b.Select(c => isolated ? c.Isolation!.WarmOpMs : c.MedianMs)]);
            double km = Stats.Median([.. k.Select(c => isolated ? c.Isolation!.WarmOpMs : c.MedianMs)]);
            double noise = Math.Max(Noise(b, bm), Noise(k, km));
            double delta = 100 * (km - bm) / bm;
            double threshold = Math.Max(minEffectPercent, 2 * noise);
            // Between-run variance is unknown with fewer than 3 runs per side: an A/A test under load
            // produced false "SLOWER" verdicts from within-run MAD alone, so such verdicts stay tentative.
            bool tentative = b.Count < 3 || k.Count < 3;
            string verdict = isolated ? "single-op (see RSS)"
                : Math.Abs(delta) <= threshold ? "~ inconclusive"
                : (delta < 0 ? "faster" : "SLOWER") + (tentative ? "? (need ≥3 runs/side)" : "");
            double ba = Stats.Median([.. b.Select(c => c.AllocatedBytesPerOp)]);
            double ka = Stats.Median([.. k.Select(c => c.AllocatedBytesPerOp)]);
            string allocDelta = ba == 0 ? (ka == 0 ? "0%" : "+∞") : string.Create(Inv, $"{100 * (ka - ba) / ba:+0.0;-0.0}%");
            sb.Append(Inv, $"| {id} | {bm:F3} | {km:F3} | {delta:+0.0;-0.0}% | {threshold:F1}% | {verdict} | {Bytes(ba)} | {Bytes(ka)} | {allocDelta} |\n");
            if (isolated)
            {
                long bp = (long)Stats.Median([.. b.Select(c => (double)c.Isolation!.PeakRssBytes)]);
                long kp = (long)Stats.Median([.. k.Select(c => (double)c.Isolation!.PeakRssBytes)]);
                sb.Append(Inv, $"| {id} peak RSS | {Bytes(bp)} | {Bytes(kp)} | {100.0 * (kp - bp) / bp:+0.0;-0.0}% | | | | | |\n");
            }
        }

        Console.Out.Write(sb.ToString());
        return 0;
    }

    // Relative noise (%): spread of per-run medians when several runs exist, else within-run MAD.
    private static double Noise(List<CaseResult> runs, double median)
    {
        double within = Stats.Median([.. runs.Select(c => c.MedianMs > 0 ? 100 * c.MadMs / c.MedianMs : 0)]);
        if (runs.Count < 2 || runs[0].Isolated)
        {
            return within;
        }

        double between = 100 * Stats.Mad([.. runs.Select(c => c.MedianMs)]) / median;
        return Math.Max(within, between);
    }

    private static string Describe(List<RunResult> runs) =>
        string.Join("; ", runs.Select(r => $"{r.Label ?? r.StartedUtc} rev {r.Git.Revision} dirty {r.Git.Dirty} XLSight.dll {Short(r.Build.Assemblies.GetValueOrDefault("XLSight")?.Sha256)} ({r.Status}, {r.Timing.SuiteElapsedMs:F0} ms)"));

    private static RunResult Load(string path) =>
        JsonSerializer.Deserialize<RunResult>(File.ReadAllText(path), Manifest.JsonOptions)
            ?? throw new InvalidDataException($"Empty result file '{path}'.");
}
