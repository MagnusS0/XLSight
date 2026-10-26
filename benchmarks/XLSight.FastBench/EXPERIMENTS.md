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

## Baseline (manifest v1, frozen) — quiet host

Revision `5643a0c` (library sources identical to `c1452e3`), XLSight.dll sha `42fc625761d4`.
29 default cases (2 fresh-process). Workers idle; load average ≈1.0 (agent host process).

| Run set | Status | Suite wall-clock |
|---|---|---|
| baseline-1..3 | complete ×3 | 20.3, 20.4, 20.4 s |
| baseline-4..6 | complete ×3 | 20.7, 21.1, 21.0 s |

Raw files: `results/*-baseline-[1-6].json`.

### Noise characterization

- Within-run MAD: typically 0.2–5 % of the median for ≥50 ms cases; 5–20 % for sub-ms cases
  (open, readcell, readrange) and cases with background parallelism (analyze).
- **Block drift:** comparing runs 1–3 with 4–6 (identical code, back to back) gave false "SLOWER"
  verdicts of +5.7 %, +15 %, +22 %, even when per-run medians were paired, because the whole host
  slowed between blocks. So no acceptance decision is made from block comparisons.
- **Interleaved A/A** (`fastbench.sh ab HEAD 3`: base/cand alternate per round, paired per-round
  deltas, a verdict needs all rounds to agree in sign and |Δ| > max(3 %, 2 × noise)): **0 of 29 cases
  flagged**. Saved as `results/aa-interleaved-3rounds-compare.md`.
- Gate used below: `ab` with ≥3 rounds; targeted cases must be "faster" with no "SLOWER"
  elsewhere, or have a mechanism-specific explanation re-confirmed with ≥5 rounds.

## E1 — Fix Formulas-mode boundary loss and async skipped-row rollback — **KEEP**

- **Source:** Readers worker (found while mapping the hot path); reproduced independently by the lead.
- **Bug A:** in Formulas mode, a buffer refill inside `<v>…</v>` of a non-formula cell kept only
  the latest partial tag (a `<` that might start `<f>`) and skipped the complete `<v>`, so the
  cell came back Empty. Repro: `complex_workbook.xlsx` through a 13-byte-read stream returned 2
  Empty text cells. Fix: the earliest complete `<f>`/`<v>` wins; refills keep the earliest partial.
- **Bug B:** async bounded reads never committed progress over rows skipped before the range.
  Every refill rewound to the first skipped row, and the window grew until the row-size guard
  threw. Repro: `ReadCellAsync("Numbers","E100001")` on large.xlsx threw
  `MalformedWorkbookException`, while sync returned 0.590495. Fix: `ScanBuffer.CommitNoIOProgress`
  plus `SheetCursor.CommitSkippedRow`.
- **Tests:** two new regression tests (`FormulasMode_EveryChunkBoundary_MatchesUnchunkedScan`
  sweeps chunk sizes 1–80; `LargeSheetAsyncBoundedReadTests`) fail on the baseline and pass with
  the fix. 923/923 tests pass.
- **Measurement:** `fastbench.sh ab HEAD 3` (`results/E1-compare.md`):
  `range.async_large_middle_rows` 134.7 → 58.8 ms (−57 %), 13.9 MiB → 392 KiB/op (−97 %).
  All other 28 cases inconclusive (no regression).
