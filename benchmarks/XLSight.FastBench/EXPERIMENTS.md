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

## E9 — Layout: prune expired claims, sweep occupied ranges by row, early break in column merge — **KEEP**

- **Hypothesis (Query worker, `query-candidate-3.patch`):** three loops scanned every field or
  occupied range found so far: the header-claim check, `BuildRowIntervals` in the vector pass,
  and the column-adjacent merge. That makes stacked-table sheets quadratic in table count.
  - Header rows ascend and a claim suppresses only rows strictly above its bottom
    (`IsInsideClaimedField`: `headerRow < BottomRight.Row`), so expired claims can be dropped.
  - Cells come from the row-major CSR store, so occupied ranges can enter and leave an active set.
  - Merge candidates are ordered by top row, and a merge needs equal top rows and never changes
    them, so the inner loop can stop at the first larger top row.
- **Correctness:** `eqcheck.sh` full JSON dumps over 19 workbooks: **identical**. Tests 958/958.
- **Measurement:** the fast-suite layout fixtures are too small for this effect. A focused `ab
  HEAD 5` on layout cases was inconclusive under heavy external host noise (±13–63 %,
  `results/E9-compare-focused.md`). A direct probe on the worker's larger fixtures, run in
  alternating base/candidate processes (`results/E9-layout-probe.md`): stack3200 463–498 →
  222–231 ms (**−55 %**, both rounds). Sparse sheets were unchanged, as expected: their
  superlinear terms are in E10's routines.

## E10 — Layout: indexed lookups instead of all-fields scans in four routines — **KEEP**

- **Hypothesis (Query worker, `query-candidate-4.patch`):** on sparse, fragmented sheets layout
  inference was worse than quadratic (worker scaling: 0.94 s → 4.6 s → 40 s as rows doubled from
  6,250 to 25,000). Four routines scanned every field or component:
  - `ClassifyColumnZone` (every field, for every column of every field) → a per-column index
    sorted by top row with a running max of bottom rows. It returns the same "first field in
    list order" the loop returned.
  - `InheritHorizontalContext` (`Where().MaxBy()` over all axes per field, plus an all-fields host
    search) → a binary search over primary horizontals. `SnapshotAxes` is ordered by top row then
    column, and the backward scan keeps `MaxBy`'s first-of-ties choice. Hosts are bucketed by left
    column, which is exact because a host must start at `fragment.Right + 1`.
  - `FindConnectedComponents` (all pairs) → bucket by left column. `VectorsConnect` is
    symmetric and union-find ignores pair order.
  - `MergeOverlappingComponents` (restart from the top after every merge) → an incremental
    merge with column buckets. It reproduces the original "lowest i, then lowest j, restart"
    merge order exactly (worker differential fuzz: 20 K random cases, ~295 K merges, duplicates
    included).
  - The lead checked each invariant in the source: `fieldRanges` is never mutated after
    construction, the original loop returns at its first match, and the ordering and symmetry
    hold as stated.
- **Correctness:** `eqcheck.sh` full JSON dumps over 19 workbooks: **identical** (the candidate's
  dump also ran 23 s → 9 s). Tests 958/958.
- **Measurement:** direct probe, alternating processes (`results/E10-layout-probe.md`):

  | Sheet | Base | Cand |
  |---|--:|--:|
  | sparse6250 | 1,111–1,122 ms | 194–220 ms (−82 %) |
  | sparse12500 | 5,077–5,318 ms | 358–407 ms (−93 %) |
  | mixed_sparse (25 K rows, FastBench fixture) | 12,880–13,038 ms | 931–1,022 ms (−93 %) |
  | stack3200 / t60k | unchanged | unchanged |

  Fast suite (`results/E10-compare-focused.md`, noisy host): `layout.sparse_fragmented_2k`
  −28.9 % "faster"; other layout/analysis cases inconclusive.
- **Process note:** a first probe attempt reused `dotnet run` file-app build caches and measured
  stale binaries. It was caught because the base numbers did not reflect the already-merged E9,
  and it was discarded. Probes are now rebuilt with `--no-cache`.

## E11 — Analysis sink: decode shared strings only for block-start spans — **KEEP (allocation)**

- **Hypothesis (Query worker, `query-candidate-2.patch`):** the analysis sink materialized every
  shared string (`AnalysisSink.cs` ~206) to compute a value-like count per row span. That count is
  only read when a span opens a region block (`TopRowValueLike`). Column profiling already works
  from the SST index and character count (`RecordSharedString`). Recording the index and decoding
  lazily only for block-start spans removes the per-cell string.
- **Correctness:** `eqcheck.sh` full JSON dumps over 19 workbooks: **identical**. Tests 958/958.
- **Measurement:** allocation probe (`results/E11-analysis-alloc-probe.md`): Analyze on
  strings_unique (Observed or Full) 19.94 → 6.42 MiB (**−68 %**); strings_repeated 0.58 →
  0.46 MiB (−21 %); Banking unchanged. Fast suite (`results/E11-compare.md`):
  `analyze.observed_strings_repeated` −21 % allocation, time neutral.
  `query.groupby_category` "SLOWER" +11 % was re-run with 5 rounds
  (`results/E11-recheck-5rounds.md`): inconclusive (−0.2 %). HEADER ROW queries never use the
  analysis sink, so this is not a regression.
  `analyze.full_merged_regions` "faster" −11 % is not claimed.

## E12 — Cache a shared string only on its second lookup (TinyLFU-style doorkeeper) — **REJECT**

- **Hypothesis (Memory worker, `memory-candidate-3.patch`):** strings looked up once (IDs, free text)
  are also kept as a UTF-16 copy beside the UTF-8 arena. Admitting to the cache only on the second
  touch (Einziger et al., "TinyLFU", ACM TOS 2017, https://arxiv.org/abs/1512.00727; applying it
  to the SST cache is the worker's inference) should cut retained memory.
- **Tests:** 958/958.
- **Measurement** (`results/E12-sst-second-touch-probe.md`, strings_unique.xlsx, 2 alternating rounds):

  | Metric | Base | Cand |
  |---|--:|--:|
  | Retained after one scan | 17.83 MiB | 7.54 MiB (−58 %) |
  | **Peak RSS, one scan** | 61.0 MiB | **68.1 MiB (+12 %)** |
  | Second scan, allocation | 2.28 MiB | **12.56 MiB (+5.5×)** |
  | Scan time | 650 / 242–278 ms | 647–651 / 241–274 ms |

- **Decision:** reject.
  - The retained-heap win is real, but peak process memory got worse: uncached first-touch strings
    become garbage the GC absorbs by growing gen0 first.
  - Any second pass (analysis then query, `HEADER AUTO`) re-allocates every singleton.
  - It weakens the documented `TryGetSharedStringId` contract ("the identical string object").
  - The remaining retained-memory problem after Dispose is handled separately (wave-2 Memory
    worker, Dispose release).

## Interim cumulative check — HEAD `937d7d8` (E1–E11 kept) vs baseline `5643a0c`

`ab 5643a0c 3` (`results/interim-cumulative-vs-baseline.md`), with three niced wave-2 workers running.
"faster" with all rounds agreeing: async bounded range −69 % (alloc −98.5 %), merged-regions
analysis −97 %, sparse layout −67 %, bounded range −31 %, ReadRange medium −21 %, queries −12 to
−18 % (expression group/order/limit, row order/limit, distinct), mixed values scan −12 %.
Allocation/op: open −70 %, readcell −69 %, numeric scan −69 %, mixed scans −44/−45 %, query cases
−21 to −67 %, Banking analysis −19 %. Single-op cold cases and full numeric scans were too noisy
to read here; a final quiet remeasure follows after wave 2. One base round came out incomplete:
the slower baseline under host load overran the 24 s internal budget (21.1–22.8 s observed).

## E13 — `<c>` attribute fast path — **KEEP (CPU, provisional until the final quiet run)**

- **Hypothesis (wave-2 Readers worker, `w2-readers-1.patch`):** almost every `<c>` start tag uses the
  exact shape ` r="A1" s="3" t="s">`. A strict single pass with inline digit and letter
  arithmetic is cheaper than the general tokenizer, which calls `Utf8Parser`. The general tokenizer
  (`ParseCellAttrsGeneral`) is kept unchanged and handles every shape the fast path declines
  (other attributes, whitespace variants, quotes, entities, overflow, unknown `t`).
- **Correctness:** `CellAttributeParserTests`: 14 accepted shapes, 36 declined, and 200,000 random
  attribute lists, each compared with the general parser. Full suite 1009/1009. `eqcheck.sh`:
  **identical**. The worker's differential check covered sync and async scans, ranges, and Analyze
  over 19 workbooks, plus chunk sweeps (1–4093-byte reads): identical.
- **Measurement:**
  - Fast-path acceptance (worker probe): 100 % of cells on large, Banking, McDonalds and the
    generated fixtures; 79.7 % on complex_workbook.
  - Microbenchmark: about 30 vs 60–70 ns per cell, which is 15 vs 32 ms over the 500K cells of
    large.xlsx.
  - Fast suite, 3 rounds (`results/E13-compare.md`): "faster" with every round agreeing on
    numeric xlsx scan −21 %, McDonalds scan −27 %, snapshot stream −9 %, and three queries
    −8 to −18 %.
  - 5-round recheck of the scan and stream cases (`results/E13-recheck-5rounds.md`):
    **inconclusive**. Noise was ±7–36 % because the two other wave-2 workers were running; the
    numeric xlsx scan was −8 %.
  - The untouched XLSB control moved −33 % in the 3-round run, so those 3-round magnitudes are
    inflated by host drift.
- **Decision:** keep. The microbenchmark gives the per-cell saving directly, and no case regressed.
  The final quiet cumulative run re-measures it.

## E14 — SharedStringTable publication ordering — **KEEP (correctness)**

- **Issue (wave-2 Memory worker, `w2-memory-3.patch`):** lock-free readers checked
  `index >= _parsedCount && !_isComplete` with plain reads. The pump also published
  `_parsedCount` and `_isComplete` with plain writes. This has two risks:
  - On weakly ordered CPUs, a reader can see a count before the arena bytes it covers.
  - The JIT may coalesce adjacent non-volatile reads, so a reader could return "" for an entry
    that was already parsed.
  - Source: dotnet/runtime `docs/design/specs/Memory-model.md`, read 2026-10-27. Applying the
    reordering rule to ARM64 is the worker's inference.
- **Change:**
  - The pump publishes each entry with `Volatile.Write(_parsedCount)` after writing its data,
    and completion with `Volatile.Write(_isComplete)` after the final count.
  - `EnsureParsed` returns an acquire-read of the count, and all three lookups compare against
    that returned value.
  - XLSB's table already used volatile fields and is unchanged.
- **Tests:** 1009/1009. No timing claim: on x64 these are plain loads and stores.

## E15 — Disposed workbook releases its shared-string table and analyzer metadata — **KEEP (retained memory)**

- **Issue (wave-2 Memory worker, `w2-memory-1.patch`):** `Dispose` disposed the SST's parser
  resources, but the reader kept the table itself (UTF-8 arena, string cache) and the analyzer
  metadata. While anything still held the workbook, the whole table stayed alive.
- **Change:** `WorkbookReaderBase.ReleaseCachedState()` runs from both `Dispose` and
  `DisposeAsync`:
  - It swaps the `Lazy` for a static sentinel with `Interlocked.Exchange`, so racing dispose calls
    cannot dispose the table twice.
  - The sentinel's factory throws `ObjectDisposedException`.
  - It disposes the old table and clears the metadata.
  - Cursors opened earlier keep their own references and behave as before.
- **Behaviour change:** an operation already running when the workbook is disposed now gets
  `ObjectDisposedException` on its next SST access. Before, it silently got "".
- **Tests:** 1015/1015. The new `WorkbookDisposeRetentionTests` use `WeakReference` checks for xlsx,
  xlsb and `DisposeAsync`, plus open-cursor continuity. They fail on the old Dispose behaviour.
- **Measurement** (`results/E15-dispose-retention-probe.md`, re-run by the lead), retained after
  Dispose with the reference held:

  | Case | Base | Cand |
  |---|--:|--:|
  | strings_unique full scan | 17,784 KiB | 15 KiB |
  | strings_unique Analyze | 6,268 KiB | 13 KiB |
  | string_heavy.xlsb scan | 39 KiB | 9 KiB |

## E16 — XLSB cursor: grow the cell buffer on demand, default input window — **KEEP (allocation)**

- **Hypothesis (wave-2 Memory worker, `w2-memory-2.patch`):** each XLSB cursor rented all
  16,384 cells (384 KiB, on the LOH). Unbounded reads also used a 1 MiB input window. Returned pool
  arrays can stay in thread-local and per-core slots until gen2 trimming, so every cursor holds
  about 1.4 MiB. Source: dotnet/runtime `SharedArrayPool.cs`, read 2026-10-27.
- **Change:**
  - The cell buffer starts at min(256, the bounded range's right column) and doubles as needed
    up to 16,384. It is cleared when rented and returned with `clearArray: true`.
  - Unbounded reads use the iterator's default 32 KiB window.
- **Tests:** 1018/1018. The new wide-row test covers growth mid-row, out-of-order columns, column
  16,384, no leak into the next row, and the projection path.
- **Measurement** (`results/E16-xlsb-cursor-probe.md`, re-run by the lead):
  - First cold cursor: 1024 → 6 KiB.
  - 4 readers open at once: 4256 → 178 KiB.
  - Steady-state alloc/op in the suite is unchanged, because the pool reuses arrays.
  - Time: full-scan minimums were base 144.7–149.7 ms and cand 148.6–151.5 ms. A cost of up to
    about 2 % is possible but sits inside the noise.
  - The 11-round paired run (`results/E16-xlsb-11rounds.md`) was inconclusive at ±93 % noise
    because the Query worker was running.
- **Decision:** keep for the allocation. The final quiet run re-checks `scan.numeric_xlsb`.

## E17 — Defer the shared-string parser start until the first lookup — **KEEP (allocation)**

- **Hypothesis (wave-2 Memory worker, `w2-memory-4.patch` minus the E14 hunks):** opening an
  XLSX cursor built the SST parse state eagerly: arena, info chunk, cache directory and the first
  64 KB inflate. That cost 66–129 KiB and 24–115 µs even when no `t="s"` cell was ever read.
- **Change:**
  - `SharedStringsByteParser.Parse` only stores the stream.
  - Under the pump lock, `StartParserLocked` primes the window, reads `uniqueCount` and sizes the
    cache before the first count is published, so lock-free readers that see a non-zero count
    also see the arrays.
  - A start failure is cached in an `ExceptionDispatchInfo` and rethrown by later lookups.
- **Behaviour change:** a corrupt SST header now fails at the first shared-string lookup, not when
  the cursor opens. A sheet that has no `t="s"` cells no longer fails at all.
- **Tests:** 1022/1022. The new `SharedStringDeferralTests` check four things:
  - no reads happen before the first lookup;
  - Dispose before any lookup releases the stream;
  - a start failure is rethrown;
  - a numeric ReadCell does not start the parser.

  `eqcheck.sh`: identical.
- **Measurement** (`results/E17-readcell-probe.md`): ReadCell on a non-string cell allocates
  32.3 KiB instead of 98–161 KiB (−67 to −80 %). p10 time is equal or lower.
  - Fast suite (`results/E17-compare-open-readcell-range.md`, 5 rounds): allocation is unchanged,
    because no default case reads an SST workbook without a string lookup.
  - The `open.small` "faster" verdict there is not claimed: open does not touch the SST.
- **Test debt noted (not changed):** a helper near line 1036 of `XlsxSheetScannerTests.cs` disposes
  the stream it hands to the table. That is safe only because its fixtures have no SST.
