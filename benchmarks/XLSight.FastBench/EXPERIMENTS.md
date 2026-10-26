# Optimization campaign — experiment log

Host: 4 vCPU Linux container (Ubuntu 24.04), .NET 10.0.13 runtime / SDK 10.0.113, workstation
non-concurrent GC. Starting revision: `c1452e3` (XLSight 0.8.5 / Query 0.4.0), no local source
changes. Raw result files live under `artifacts/fastbench/` (gitignored); the files cited as
evidence are copied to `benchmarks/XLSight.FastBench/results/`.

Acceptance gate: identical manifest and fixtures on both sides; ≥3 alternating `ab` rounds on a
quiet host; the time effect must exceed max(3 %, 2 × observed noise) on the targeted cases with no
unexplained regression elsewhere; all tests green; allocation deltas explained.

## E0 — Infrastructure (no library change)

| Step | Command | Result |
|---|---|---|
| Restore | `fastbench.sh restore` | locked restore, `CI=true` |
| Build | `fastbench.sh build` | ~3 s incremental, ~19 s cold solution |
| Fixtures | `fastbench.sh fixtures --force` | 3 workbooks (2.0–2.6 MiB), 2.4 s; SHA-256 identical across regenerations |
| Calibration | `dotnet …/XLSight.FastBench.dll record-expected` | pinned digests for 26 cases + 2 probes on `be8bf6f` |
| Smoke | `fastbench.sh run --label smoke2` | complete, 21.1 s wall (host shared with 3 workers) |
| Self-test | `fastbench.sh selftest` | all 6 deadline/failure paths as designed (exit 3, 3, 4, 5, 2, 1) |

### A/A check under load (`fastbench.sh ab HEAD 1`)

Same commit both sides; both builds produced the identical `XLSight.dll` (sha `7f8b46920f21`).
With 3 CPU-heavy workers running, single-run comparison flagged 3 of 27 cases as "SLOWER" (+5.7 %,
+35.5 %, +53.4 %) that are pure noise. Consequences: single-run verdicts are now marked tentative,
acceptance requires ≥3 rounds per side, and decisions are only made on a quiet host. The run also
showed allocation counts are near- but not perfectly deterministic (`scan.numeric_xlsb` 38.2 vs
28.6 KiB/op): `GetTotalAllocatedBytes` includes runtime background-thread allocations, so deltas
below ~10 KiB/op on multi-MiB cases are noise.
