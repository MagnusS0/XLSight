using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XLSight.FastBench;

internal sealed class Manifest
{
    public required string Version { get; init; }
    public string? Description { get; init; }

    /// <summary>Internal soft deadline for the whole suite, measured from process start.</summary>
    public int SuiteBudgetMs { get; init; } = 24_000;

    /// <summary>Time held back from case scheduling for validation output, serialization and shutdown.</summary>
    public int ReserveMs { get; init; } = 1_200;

    /// <summary>In-process hard deadline: a timer thread writes an incomplete result and exits.</summary>
    public int HardDeadlineMs { get; init; } = 27_500;

    public required List<FixtureDef> Fixtures { get; init; }
    public required List<CaseDef> Cases { get; init; }

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static (Manifest Manifest, string Sha256) Load(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        Manifest manifest = JsonSerializer.Deserialize<Manifest>(bytes, JsonOptions)
            ?? throw new InvalidDataException($"Manifest '{path}' is empty.");
        return (manifest, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }
}

internal sealed class FixtureDef
{
    public required string Name { get; init; }

    /// <summary>Path relative to the repository root.</summary>
    public required string Path { get; init; }

    public string? Sha256 { get; set; }

    /// <summary>Name of the deterministic generator, or null for a checked-in workbook.</summary>
    public string? Generator { get; init; }
}

internal sealed class CaseDef
{
    public required string Id { get; init; }
    public required string Op { get; init; }
    public string? Fixture { get; init; }
    public string? Sheet { get; init; }
    public string? Range { get; init; }
    public string? Address { get; init; }
    public string? Mode { get; init; }
    public string? Level { get; init; }
    public string? Query { get; init; }
    public string? Column { get; init; }
    public string? SumColumn { get; init; }
    public double Threshold { get; init; }
    public int HeaderRow { get; init; }
    public int Top { get; init; } = 100;
    public int SleepMs { get; init; }

    /// <summary>Default-suite member. Probe cases run only when selected by filter.</summary>
    public bool Default { get; init; } = true;

    /// <summary>Run in a fresh child process to attribute peak RSS and cold-start cost.</summary>
    public bool Isolated { get; init; }

    public int OpsPerSample { get; init; } = 1;
    public int WarmupMs { get; init; } = 100;
    public int BudgetMs { get; init; } = 800;
    public int MinSamples { get; init; } = 5;
    public int MaxSamples { get; init; } = 25;
    public string Unit { get; init; } = "ops";
    public List<string>? Tags { get; init; }
    public Expectation? Expected { get; set; }
}

internal sealed class Expectation
{
    public required string Digest { get; init; }
    public required string DeepDigest { get; init; }
    public long Units { get; init; }
}
