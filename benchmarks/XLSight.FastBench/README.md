# XLSight FastBench

A capped, cross-component regression benchmark for optimize–measure–validate loops. The whole
default suite (process start to exit, including fixture hashing, warmup, sampling, validation,
serialization and two fresh-process memory cases) is designed to finish in **20–25 s** and is hard
capped at **30 s**. The BenchmarkDotNet suite in `../XLSight.Benchmarks` remains the tool for deep
confirmation.

## Commands

All commands go through `fastbench.sh` (bash, Linux/macOS with coreutils `timeout`).

| Step | Command | Notes |
|---|---|---|
| Restore | `benchmarks/XLSight.FastBench/fastbench.sh restore` | locked-mode restore (`CI=true`, matching CI lock files) |
| Build | `benchmarks/XLSight.FastBench/fastbench.sh build` | Release build of runner + libraries; rerun after every source edit |
| Fixtures | `benchmarks/XLSight.FastBench/fastbench.sh fixtures` | generates the 3 synthetic workbooks into `artifacts/fastbench/fixtures/` and checks their pinned SHA-256 |
| All prep | `benchmarks/XLSight.FastBench/fastbench.sh prepare` | the three above, each timed separately |
| **Fast suite** | `benchmarks/XLSight.FastBench/fastbench.sh run [--label NAME] [--filter REGEX]` | never builds, restores or generates; refuses a stale binary |
| Compare | `benchmarks/XLSight.FastBench/fastbench.sh compare --base A.json [A2.json…] --cand B.json [B2.json…]` | refuses runs with different manifests or fixture bytes |
| A/B | `benchmarks/XLSight.FastBench/fastbench.sh ab <base-ref> [rounds] [run args]` | builds the *current* runner + manifest against `<base-ref>` library sources in a temporary worktree, then alternates base/candidate runs and compares |
| Confirm tier | `fastbench.sh fixtures --confirm`, then `fastbench.sh confirm [REGEX]` | large fixtures (600 K-row strings, 200 K-row mixed), fresh-process cold/peak-RSS cases and long warm sampling; ≈1 min, 200 s cap; not part of the fast suite |
| Self-test | `benchmarks/XLSight.FastBench/fastbench.sh selftest` | demonstrates every deadline/failure path (≈45 s, not part of the fast suite) |

A/B on the confirm tier: `FASTBENCH_WALL_CAP_S=200 fastbench.sh ab <ref> 3 --filter '^confirm\.' --budget-ms 180000`.
With fewer than 3 runs per side, `compare` verdicts are marked tentative: an A/A check under load
produced false "SLOWER" verdicts from within-run MAD alone.

`--filter` matches case ids or tags (e.g. `--filter query`, `--filter '^scan\.'`). Filtered runs
are for iteration; acceptance decisions use the default suite.

Results land in `artifacts/fastbench/`: `<utc>-<label>.json` (machine-readable), `<utc>-<label>.md`
(table) and `latest.json`.

## What is measured

Each case is one operation from a frozen manifest (`manifest.json`). Every operation opens its own
workbook from a file path, so open cost is part of every number.

- **Warm steady state** (in-process). The first op is the untimed deep validation and also warms
  up the JIT (reported as `first-op ms` for reference only). Further cheap ops follow until
  `warmupMs`, then `opsPerSample` ops per sample until `budgetMs` or `maxSamples`, and never fewer
  than `minSamples` (5) unless the suite deadline intervenes, which marks the case incomplete.
  Reports the median ms/op, the MAD (median absolute deviation, shown as a % of the median),
  throughput in a stated unit (cells, rows, sheets) per second, allocated bytes/op and gen0/1/2
  collections per op.
- **Cold, fresh process** (`isolated: true`). The parent spawns a child process that runs one cold
  op, one warm op and one deep validation op. It reports the cold op time (including JIT), the warm
  op time, the cold op's allocations, baseline and peak RSS, and the managed heap still live after a
  full GC.
- **Allocation** is `GC.GetTotalAllocatedBytes(precise: true)` deltas, covering every thread, so
  the parallel `Analyze` is fully counted.
- **Peak RSS** is the process `VmHWM` from `/proc/self/status`, which includes the runtime's own
  ~40 MiB baseline (reported separately). Total allocation, live managed memory and RSS are three
  different quantities and are never substituted for one another.

### Correctness

Each op folds everything it produced into a 64-bit digest: cell types, numeric bits, date ticks,
text lengths, row/column positions, and aggregate and profile fields. The manifest pins the
expected digest and work units for every op, and every timed op is checked against them. A
separate deep digest that also hashes every character of every string is checked once per case,
outside the timed region but inside the suite deadline. Expectations are recorded once per
manifest version on the baseline build with `dotnet …/XLSight.FastBench.dll record-expected`. That
is a calibration step: never run it on a candidate.

### Deadlines

1. The internal suite deadline (`suiteBudgetMs`, 24 s from process start) is tracked on a
   monotonic `Stopwatch` plus a one-time startup estimate. Each case gets a bounded slice, and
   `reserveMs` is held back for output.
2. An internal hard deadline (`hardDeadlineMs`, 27.5 s) is a timer thread that marks running cases
   incomplete, writes the result file and exits with code 4, even when an operation never returns.
3. The external watchdog, coreutils `timeout 30s`, signals the runner's whole process group
   (isolated children included). The driver then writes a `*-watchdog-killed.json` stub and exits
   with code 5.

Exit codes: 0 complete and valid · 1 usage error or stale build · 2 validation or fixture failure ·
3 incomplete (deadline, skipped or unverified case) · 4 internal hard deadline · 5 external
watchdog or wall-clock cap exceeded. A run that is not `complete` is never a valid measurement.

## Case matrix (manifest v1)

| Area | Cases | Fixture |
|---|---|---|
| Open / early exit | `open.small`, `open.banking_real`, `readcell.large_early_exit` | small.xlsx, real 22-sheet workbook, 21 MB-XML numeric sheet |
| Numeric scans | `scan.numeric_xlsx`, `scan.numeric_xlsb` | large.xlsx / large.xlsb (identical content, identical digests) |
| Strings | `scan.strings_repeated` (8/50/2,000-value SST columns), `scan.strings_unique` (160 K unique SST entries, non-ASCII, surrogate pairs, entities, rich text), `scan.strings_xlsb` | generated / string_heavy.xlsb |
| Mixed / formulas / sparse | `scan.mixed_sparse_values`, `scan.mixed_sparse_formulas`, `stream.snapshots_mixed` | generated: 40% fill A:Z + far column, every cell type, formulas |
| Real workbook | `scan.real_mcdonalds_all` | 10_McDonalds_golden.xlsx, all sheets |
| Bounded ranges | `range.large_middle_rows` (rows 60,001–60,200), `range.readrange_medium` | large.xlsx, medium.xlsx |
| Analysis | `analyze.full_banking_real`, `analyze.observed_strings_repeated` | real / generated |
| Layout | `layout.complex_calculator`, `layout.mcdonalds_all` | complex_workbook.xlsx, real |
| Query | builder filter+sum, group by, expression group by + order by + limit, row order by + limit, limit early exit, distinct | large.xlsx, generated |
| Memory / cold | `mem.cold_scan_strings_unique`, `mem.cold_analyze_banking_real` | fresh processes |
| Probes (not default) | `probe.over_budget`, `probe.hang` | used by `selftest` |

### Coverage gaps

- There is no async API case and no `Analyze(Exact)`-only case.
- XLSB has no generated fixture, so it is covered only by the checked-in numeric and string
  workbooks. There is no XLSB formula case.
- There are no shared-formula (`<f t="shared">`), VBA, chart or pivot workloads.
- The fixtures are a few MB each, so large-scale memory behavior (e.g. the 1 M-row NYC workbook in
  the top-level README) needs the BenchmarkDotNet `XlLarge` cases or a focused large run.
- Effects smaller than about 2× a case's MAD are reported as inconclusive. Confirm them with
  repeated `ab` rounds and longer runs.
