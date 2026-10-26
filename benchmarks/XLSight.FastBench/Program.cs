using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using XLSight.FastBench;

// Commands:
//   run      [--filter REGEX] [--label NAME] [--out DIR] [--budget-ms N] [--no-internal-watchdog]
//   compare  --base A.json [A2.json ...] --cand B.json [B2.json ...] [--min-effect PCT] [--paired]
//   fixtures [--force] [--confirm]  generate deterministic synthetic fixtures (--confirm adds the large tier)
//   record-expected             calibration: pin fixture hashes and expected digests into the manifest
//   child    --case ID          internal: one isolated case in a fresh process
try
{
    string command = args.Length > 0 ? args[0] : "run";
    var options = ParseOptions(args.Skip(1).ToArray());
    string root = options.TryGetValue("root", out List<string>? r) ? Path.GetFullPath(r[0]) : FindRoot();
    string manifestPath = options.TryGetValue("manifest", out List<string>? m)
        ? Path.GetFullPath(m[0])
        : Path.Combine(root, "benchmarks", "XLSight.FastBench", "manifest.json");

    return command switch
    {
        "run" => new Runner(new RunOptions
        {
            Root = root,
            ManifestPath = manifestPath,
            OutDir = options.TryGetValue("out", out List<string>? o) ? Path.GetFullPath(o[0]) : Path.Combine(root, "artifacts", "fastbench"),
            Filter = Single(options, "filter"),
            Label = Single(options, "label"),
            GitRevision = Single(options, "git-rev"),
            GitDirty = Single(options, "git-dirty"),
            SuiteBudgetOverrideMs = Single(options, "budget-ms") is { } b ? int.Parse(b, CultureInfo.InvariantCulture) : null,
            InternalWatchdog = !options.ContainsKey("no-internal-watchdog"),
        }).Run(),
        "child" => Runner.RunChild(root, manifestPath, Single(options, "case") ?? throw new ArgumentException("--case is required")),
        "compare" => Report.Compare(
            options.GetValueOrDefault("base") ?? throw new ArgumentException("--base is required"),
            options.GetValueOrDefault("cand") ?? throw new ArgumentException("--cand is required"),
            Single(options, "min-effect") is { } e ? double.Parse(e, CultureInfo.InvariantCulture) : 3.0,
            options.ContainsKey("paired")),
        "fixtures" => GenerateFixtures(root, manifestPath, options.ContainsKey("force"), options.ContainsKey("confirm")),
        "record-expected" => RecordExpected(root, manifestPath),
        _ => throw new ArgumentException($"Unknown command '{command}'."),
    };
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static Dictionary<string, List<string>> ParseOptions(string[] args)
{
    var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
    List<string>? current = null;
    foreach (string arg in args)
    {
        if (arg.StartsWith("--", StringComparison.Ordinal))
        {
            current = [];
            result[arg[2..]] = current;
        }
        else if (current is not null)
        {
            current.Add(arg);
        }
        else
        {
            throw new ArgumentException($"Unexpected argument '{arg}'.");
        }
    }

    return result;
}

static string? Single(Dictionary<string, List<string>> options, string key) =>
    options.TryGetValue(key, out List<string>? values) && values.Count > 0 ? values[0] : null;

static string FindRoot()
{
    for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "XLSight.slnx")))
        {
            return dir.FullName;
        }
    }

    throw new ArgumentException("Could not locate the repository root (XLSight.slnx); pass --root.");
}

static int GenerateFixtures(string root, string manifestPath, bool force, bool confirmTier)
{
    (Manifest manifest, _) = Manifest.Load(manifestPath);
    int mismatches = 0;
    foreach (FixtureDef fixture in manifest.Fixtures.Where(f => f.Generator is not null && (confirmTier || f.Tier != "confirm")))
    {
        string path = Path.Combine(root, fixture.Path);
        var sw = Stopwatch.StartNew();
        bool generated = false;
        if (force || !File.Exists(path))
        {
            FixtureGenerator.Generate(fixture.Generator!, path);
            generated = true;
        }

        string sha = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        bool ok = fixture.Sha256 is null || fixture.Sha256 == sha;
        mismatches += ok ? 0 : 1;
        Console.WriteLine($"{fixture.Name}: {(generated ? "generated" : "present")} {new FileInfo(path).Length / 1024} KiB sha256 {sha} {(fixture.Sha256 is null ? "(unpinned)" : ok ? "(matches manifest)" : "(MISMATCH: manifest " + fixture.Sha256 + ")")} in {sw.ElapsedMilliseconds} ms");
    }

    return mismatches == 0 ? 0 : 2;
}

// Calibration step, run once per manifest version on the baseline build: pins fixture hashes and
// the expected cheap/deep digests and work units of every case. Never run it on a candidate.
static int RecordExpected(string root, string manifestPath)
{
    (Manifest manifest, _) = Manifest.Load(manifestPath);
    HashSet<string> present = new(StringComparer.Ordinal);
    foreach (FixtureDef fixture in manifest.Fixtures)
    {
        string fixturePath = Path.Combine(root, fixture.Path);
        if (!File.Exists(fixturePath))
        {
            Console.WriteLine($"{fixture.Name}: absent, expectations for its cases left unchanged");
            continue;
        }

        present.Add(fixture.Name);
        fixture.Sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(fixturePath)));
    }

    foreach (CaseDef def in manifest.Cases.Where(c => c.Op != "sleep" && (c.Fixture is null || present.Contains(c.Fixture))))
    {
        string? path = def.Fixture is null ? null : Path.Combine(root, manifest.Fixtures.Single(f => f.Name == def.Fixture).Path);
        var sw = Stopwatch.StartNew();
        OpResult cheap = Operations.Run(def, path, deep: false);
        double firstMs = sw.Elapsed.TotalMilliseconds;
        OpResult deep = Operations.Run(def, path, deep: true);
        def.Expected = new Expectation { Digest = Digest.Hex(cheap.Digest), DeepDigest = Digest.Hex(deep.Digest), Units = cheap.Units };
        Console.WriteLine($"{def.Id}: digest {def.Expected.Digest} deep {def.Expected.DeepDigest} units {cheap.Units} first-op {firstMs:F1} ms");
    }

    File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, Manifest.JsonOptions) + "\n");
    return 0;
}
