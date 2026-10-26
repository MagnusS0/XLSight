namespace XLSight.FastBench;

internal sealed class RunResult
{
    public string Schema { get; init; } = "xlsight-fastbench/1";
    public string Status { get; set; } = "running";
    public int ExitCode { get; set; }
    public List<string> Problems { get; } = [];
    public required string StartedUtc { get; init; }
    public string? Label { get; init; }
    public required GitIdentity Git { get; init; }
    public required BuildIdentity Build { get; init; }
    public required RuntimeIdentity Runtime { get; init; }
    public required ManifestIdentity Manifest { get; init; }
    public List<FixtureResult> Fixtures { get; } = [];
    public required SamplingPolicy Policy { get; init; }
    public SuiteTiming Timing { get; } = new();
    public List<CaseResult> Cases { get; } = [];
}

internal sealed class GitIdentity
{
    public string? Revision { get; init; }
    public string? Dirty { get; init; }
}

internal sealed class BuildIdentity
{
    public required string Configuration { get; init; }
    public required Dictionary<string, AssemblyIdentity> Assemblies { get; init; }
}

internal sealed class AssemblyIdentity
{
    public required string Sha256 { get; init; }
    public string? InformationalVersion { get; init; }
    public required string LastWriteUtc { get; init; }
}

internal sealed class RuntimeIdentity
{
    public required string Framework { get; init; }
    public required string Os { get; init; }
    public required string Architecture { get; init; }
    public required int ProcessorCount { get; init; }
    public string? CpuModel { get; init; }
    public required string Gc { get; init; }
    public required Dictionary<string, string> DotnetEnvironment { get; init; }
}

internal sealed class ManifestIdentity
{
    public required string Path { get; init; }
    public required string Version { get; init; }
    public required string Sha256 { get; init; }
    public string? Filter { get; init; }
}

internal sealed class FixtureResult
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public string? Sha256 { get; init; }
    public long Bytes { get; init; }
    public required string Status { get; init; }
}

internal sealed class SamplingPolicy
{
    public required int SuiteBudgetMs { get; init; }
    public required int ReserveMs { get; init; }
    public required int HardDeadlineMs { get; init; }
    public string Warmup { get; init; } = "first op is the untimed deep validation (JIT warmup, reported as firstOpMs); further cheap ops until warmupMs has elapsed";
    public string Sampling { get; init; } = "fixed opsPerSample; sample until budgetMs or maxSamples, never fewer than minSamples unless the suite deadline intervenes (then incomplete)";
    public string Allocation { get; init; } = "GC.GetTotalAllocatedBytes(precise: true) delta over all samples, all threads";
    public string PeakMemory { get; init; } = "isolated cases only: VmHWM from /proc/self/status in a fresh child process (whole-process RSS high-water mark, includes runtime baseline)";
}

internal sealed class SuiteTiming
{
    public double StartupMs { get; set; }
    public double SuiteElapsedMs { get; set; }
    public bool DeadlineHit { get; set; }
}

internal sealed class CaseResult
{
    public required string Id { get; init; }
    public string Status { get; set; } = "pending";
    public string? Message { get; set; }
    public List<string>? Tags { get; init; }
    public bool Isolated { get; init; }
    public required string Unit { get; init; }
    public int OpsPerSample { get; init; }
    public int WarmupOps { get; set; }
    public int Samples { get; set; }
    public double FirstOpMs { get; set; }
    public double MedianMs { get; set; }
    public double MadMs { get; set; }
    public double MinMs { get; set; }
    public double MaxMs { get; set; }
    public long UnitsPerOp { get; set; }
    public double UnitsPerSecond { get; set; }
    public double FixtureMiBPerSecond { get; set; }
    public double AllocatedBytesPerOp { get; set; }
    public double Gen0PerOp { get; set; }
    public double Gen1PerOp { get; set; }
    public double Gen2PerOp { get; set; }
    public string? Digest { get; set; }
    public string? DeepDigest { get; set; }
    public string? Validation { get; set; }
    public double CaseElapsedMs { get; set; }
    public IsolatedMetrics? Isolation { get; set; }
    public List<double>? SampleMs { get; set; }
}

internal sealed class IsolatedMetrics
{
    public double ColdOpMs { get; set; }
    public double WarmOpMs { get; set; }
    public long ColdOpAllocatedBytes { get; set; }
    public long BaselineHwmBytes { get; set; }
    public long PeakRssBytes { get; set; }
    public long RetainedManagedBytesAfterOp { get; set; }
    public int Gen0 { get; set; }
    public int Gen1 { get; set; }
    public int Gen2 { get; set; }
    public double ChildWallMs { get; set; }
}
