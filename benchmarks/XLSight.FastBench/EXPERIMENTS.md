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

## E2 — Size package-part read buffers to the entry — **KEEP (allocation)**

- **Hypothesis (Readers worker):** every buffered open of a package part allocated a fixed 64 KiB
  `BufferedStream` buffer, even for ~550-byte workbook.xml and rels parts and 2.6 KB styles
  (`ZipEntryExtensions.cs`, `XlsxPackage.cs:158`). Clamping to the declared uncompressed length
  (512 B – 64 KiB) removes ~190 KiB of dead heap per open without changing read behaviour; the
  declared length is only a sizing hint.
- **Tests:** 923/923.
- **Measurement:** `ab HEAD 3` (`results/E2-compare.md`). Allocation/op: open.small −70 %,
  readcell −69 %, scan.numeric_xlsx −69 %, range.large_middle_rows −69 %, query cases −20 to −67 %,
  analyze.full_banking_real −19 % (4.6 → 3.7 MiB), cold Banking analyze −17 %, peak RSS −1.4 %.
  Time: all inconclusive except `layout.mcdonalds_all` "faster" −13 %. That case was not
  targeted and there is no clear mechanism, so it is not claimed as an effect.

## E3 — Release shared-string parser resources at completion; lazy cache chunks — **KEEP (memory)**

- **Hypothesis (Memory worker):** the SST table never reached `_isComplete` during normal scans,
  so it held a 256 KiB staging buffer, a 64 KiB window and the zip entry stream until Dispose.
  That stream was never disposed at all (its zlib state waited for the finalizer). The table
  also allocated its 1 MiB cache-slot array up front. Fix: probe once more when `parsed ==
  declared uniqueCount` to complete the table and free its buffers; dispose the entry stream;
  allocate cache slots in 4,096-entry chunks via `Interlocked.CompareExchange`.
- **Tests:** 923/923.
- **Fast suite:** `ab HEAD 3` (`results/E3-compare.md`). No regression; allocations −1 to −3.5 %
  broadly; `query.groupby_category` "faster" −4.9 %, at the edge of noise and not claimed.
- **Focused probe** (deterministic allocation, strings_unique.xlsx, 160 K unique SST entries; script in
  `results/E3-sst-probe.txt`):

  | Workload | Base alloc | Cand alloc | Base retained (open) | Cand retained (open) |
  |---|--:|--:|--:|--:|
  | ReadRange A1:D10 | 1,245 KiB | 245 KiB (−80 %) | 1,178 KiB | 178 KiB (−85 %) |
  | Full scan | 20,197 KiB | 20,190 KiB | 17,797 KiB | 17,790 KiB |

  Still open: a disposed workbook that is still referenced retains 17.8 MiB (follow-up F-dispose).

## E4 — Clinger fast path for short decimals — **KEEP (time)**

- **Hypothesis (both the Readers and Memory workers, prototyped independently):** `Utf8Parser.TryParse`
  costs ~50–70 ns per short value and ~21 % of the numeric-scan profile. For `-?\d+(\.\d+)?` with
  ≤15 digits, the mantissa is an exact integer below 2^53 and 10^k is an exact double, so one
  IEEE division is correctly rounded (Clinger 1990; Lemire, SP&E 2021, §3,
  https://arxiv.org/abs/2101.11408). Other input falls through to `Utf8Parser`, so its lenient
  prefix semantics (`12abc` → 12) are unchanged. The Readers version was chosen as the more
  conservative of the two (`1.` and `.5` fall back).
- **Tests:** new `ShortDecimalParserTests` (accepted inputs bit-identical to `Utf8Parser`, including
  `-0`; unusual inputs declined; 200,000 random short decimals bit-identical). 947/947.
- **Measurement:**
  - Full `ab HEAD 3` (`results/E4-compare-full.md`): the host was noisy (a resident Roslyn
    `VBCSCompiler` averaged 35 % CPU). `scan.numeric_xlsx` was inconclusive; `scan.strings_repeated`
    −11 % and `query.rows_limit_early_exit` −23 % were "faster"; no regressions.
  - After fixing the driver to shut down build servers before measuring, a focused `ab HEAD 5`
    (`results/E4-compare-focused-5rounds.md`): `confirm.warm_scan_numeric_xlsx` (40 samples/run)
    152.5 → 137.1 ms (**−10.1 %**, noise ±6.2 %, all 5 rounds faster); `scan.numeric_xlsx` −16.9 %.
    Mixed, real and string scans were inconclusive, as expected from their lower numeric share.
- **Driver fix:** `fastbench.sh` now builds with `-nodeReuse:false -p:UseSharedCompilation=false`
  and runs `dotnet build-server shutdown` before every measured run.

## E5 — Single-allocation XML text decode; shared error-code strings — **KEEP (allocation)**

- **Hypothesis (Memory worker):** inline strings, `t="str"` values and formula text containing `&`
  were materialized twice (`Encoding.UTF8.GetString`, then `WebUtility.HtmlDecode`), and every
  error cell allocated a new string. Decoding the five XML entities and numeric references on the
  UTF-8 bytes into a stack or pooled buffer, then calling `GetString` once, halves that.
  Unrecognized forms (`&nbsp;`, `&#0;`, surrogates, a missing `;`, signs) keep the old path, so
  output is identical.
- **Implementation:** the decode-only part of `memory-candidate-1.patch`, applied by hand on top
  of E4 (whose number fast path superseded the Memory worker's own).
- **Tests:** new `XmlTextDecoderTests` (50,000 random entity mixes, including HTML-only names,
  malformed and out-of-range references, non-ASCII and >256-byte strings, all equal to
  `HtmlDecode(GetString(bytes))`; error codes). 949/949.
- **Measurement:** `ab HEAD 3` (`results/E5-compare.md`). Allocation/op: scan.mixed_sparse_values
  4.4 → 2.6 MiB (−41 %), scan.mixed_sparse_formulas 9.1 → 5.1 MiB (−44 %), stream.snapshots_mixed
  −10 %. Time: inconclusive on the touched cases. `layout.sparse_fragmented_2k` "faster" −18 %
  is not targeted and not claimed. `scan.numeric_xlsb` +34 % is inconclusive (±22 %), and XLSB
  does not use this decoder.

## E6 — Vectorized end-tag search for skipped rows and cells — **KEEP (time)**

- **Hypothesis (Readers worker):** rows before a bounded range, and cells outside its columns, were
  discarded with `SkipToEndTag`, which visits every tag (~8 `IndexOf` calls per cell). Searching
  for the end tag's local name with one vectorized `IndexOf`, and accepting a hit only for
  `</[prefix:]name ws* >`, skips whole rows in one pass. A cell body cannot contain `</row>`
  (`<` must be escaped), so cells right of the range jump straight to the row end.
- **Tests:** new `BoundedRange_SkippedRowsAndCells_EveryChunkBoundary_MatchesFilteredFullScan`
  (prefixed tags, `</x:row >`, `row>` and escaped `</row>` in text, `</x:rowx>`; chunk sizes 1–64).
  958/958.
- **Measurement:** `ab HEAD 3` (`results/E6-compare.md`): `range.large_middle_rows` 53.2 → 34.5 ms
  (**−35.7 %**, noise ±3.1 %); `range.async_large_middle_rows` 58.9 → 41.6 ms (**−29.3 %**).
  `query.rows_orderby_limit` "faster" −5.1 % is at the edge of noise and not claimed. No regressions.

## E7 — One tag walk over the post-sheetData metadata — **KEEP (time; removes quadratic cost)**

- **Hypothesis (Readers worker):** `TryScanPostSheetData` ran four whole-window `TryFindStartTag`
  searches (mergeCell, conditionalFormatting, dataValidation, hyperlink) per dispatched element:
  O(elements × window). Walking `<` to `<` and classifying each tag once makes it linear.
  Only analysis sinks call it (`XlsxSheetScanner.cs:104`); plain cursors never do.
- **Tests:** 958/958. The Readers worker also compared analysis output across 43 sheets plus the
  synthetic files: identical.
- **Measurement:** `ab HEAD 3` (`results/E7-compare.md`): `analyze.full_merged_regions` (2,500
  merges + 250 validations) 176.5 → 7.3 ms (**−96 %**). `scan.strings_unique` was flagged
  "SLOWER" +5.6 %. Under the gate it was re-run with 5 focused rounds
  (`results/E7-recheck-5rounds.md`): inconclusive (+3.5 %, ±51 %), and the changed code is
  unreachable from cursor scans, so it is not a regression. Merged regions held at −94.9 %.

## E8 — Layout: O(1) matrix-run precondition; allocation-free header runs — **KEEP**

- **Hypothesis (Query worker, `query-candidate-1.patch`):** `FindMatrixFields` ran a row and column
  binary search (`FindCoordinateFirstRow`) for every cell before checking that a uniform-step
  run could exist. A qualifying run needs ≥3 cells, and `ExtendUniformRun` is prefix-monotone
  (uniformity is judged against the first step), so extending at most 3 cells first is an exact
  O(1) filter. `GetHeaderRuns` was an iterator allocating per call; it is now a struct enumerator.
- **Correctness:** full JSON dumps of `AnalyzeLayout` + `AnalyzeSheet(Observed/Full)` for every
  sheet of 19 workbooks (golden, test, synthetic and FastBench fixtures; 232 records), built
  against base and candidate: **identical** (`eqcheck.sh`). Tests 958/958.
- **Measurement:** `ab HEAD 3` (`results/E8-compare.md`): `layout.complex_calculator` −14.4 %
  (±6.2 %); layout allocation −14.3 % (sparse_fragmented_2k) and −7.7 % (McDonalds). Other layout
  times were inconclusive on a noisy host. The worker's larger dense-table figure (60 K rows,
  inference 127 → 39 ms) has no fast-suite case and is reported as the worker's measurement only.
  `analyze.full_merged_regions` "faster" (−14.5 %) and `scan.mixed_sparse_values` (−3.8 %) are
  outside this change's code path and not claimed.
