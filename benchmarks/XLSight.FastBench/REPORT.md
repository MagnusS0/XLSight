# XLSight performance and memory campaign: final report

Date: 2026-10-27. Baseline: `5643a0c` (library sources identical to `c1452e3`, XLSight 0.8.5 /
Query 0.4.0). Final head: the commit that adds this report on
`claude/dotnet-artisan-plugin-install-7wvc7j`.

Environment: a cloud Linux VM with .NET SDK 10.0.113 and runtime 10.0.13, which links the system
zlib 1.3, not zlib-ng. Workstation GC, non-concurrent. The host has intermittent external
noise and no steal time is visible from inside it. Every timing verdict below therefore comes
from paired, interleaved runs that require all rounds to agree in sign. `EXPERIMENTS.md` has the
per-experiment log, and `results/` has the raw data behind every number.

## 1. Outcome

Final quiet run: `fastbench.sh ab 5643a0c 5`, 5 interleaved rounds, load average 0.4–1.4
(`results/final-cumulative-vs-baseline.md`).

- **Suite wall-clock:** 19.5–19.9 s → 18.0–18.2 s. All 10 runs complete, inside the 24 s internal
  budget and the 30 s external cap.
- **19 of 29 default cases "faster"** with every round agreeing; **0 slower**. The other 10 are
  inconclusive.

| Area | Case | Time | Allocation/op |
|---|---|--:|--:|
| Async | `range.async_large_middle_rows` | **−74.5 %** | **−99.1 %** (12.0 MiB → 108 KiB) |
| Analysis | `analyze.full_merged_regions` | **−97.3 %** (199 → 5.2 ms) | −35.8 % |
| Layout | `layout.sparse_fragmented_2k` | **−67.4 %** | −19.8 % |
| Ranges | `range.large_middle_rows` | −31.4 % | −68.7 % |
| Scans | `scan.numeric_xlsx` | −25.0 % | −68.6 % |
| | `scan.strings_repeated` | −20.7 % | −39.5 % |
| | `scan.real_mcdonalds_all` | −19.8 % | −9.5 % |
| | `scan.mixed_sparse_values` | −13.4 % | −43.8 % |
| Queries | all six default query cases | −14 to −25 % | −21 to −67 % |
| Analysis | `analyze.full_banking_real` | −10.5 % | −17.1 % |
| Open / point reads | `open.small` (alloc), `readcell.large_early_exit` | −11 % (readcell) | −70 % / −69 % |
| Cold, isolated | `mem.cold_analyze_banking_real` | −18 % | −16.8 %; peak RSS +0.6 % |

The larger workloads in the confirm tier, run twice per side and interleaved
(`results/final-confirm-vs-baseline.md`):
- **Cold x10 cases:** time −21 to −24 %.
- **Warm scans:** time −10 to −22 %, allocation −40 to −69 %.
- **Peak RSS:** within ±1.2 % everywhere.

Results measured outside the suite with probes (Release re-runs; see the correction in `EXPERIMENTS.md`: the first probe tables were Debug builds):

| Probe | Before | After | File |
|---|--:|--:|---|
| Layout, mixed_sparse (25K cells), E19 only | 785–796 ms | 484–500 ms (−38 %) | `results/release-rerun/E19.txt` |
| Retained memory after Dispose, strings_unique | 17.8 MiB | 15 KiB | `results/E15-dispose-retention-probe.md` |
| First cold XLSB cursor, allocation | 1024 KiB | 6 KiB | `results/E16-xlsb-cursor-probe.md` |
| ReadCell on a non-string cell, allocation | 98–161 KiB | 32 KiB | `results/E17-readcell-probe.md` |
| Thread-pool work items per async scan | 463 | 1 (alloc/op 212 → 1–14 KiB) | `results/release-rerun/E18.txt` |

**Correctness bugs found and fixed:**
- **E1:** Formulas mode lost data at buffer boundaries.
- **E1:** the async bounded read threw on skipped-row rollback.
- **E14:** SST publication ordering was not guaranteed on weakly ordered CPUs.

## 2. What was kept

| # | Change | Kind |
|---|---|---|
| E1 | Formulas-mode boundary fix; async skipped-row rollback fix | correctness |
| E2 | Package-part read buffers sized to the entry | allocation |
| E3 | SST parser resources released at completion; lazy cache chunks | memory |
| E4 | Clinger fast path for short decimals (bit-identical to `Utf8Parser`) | CPU |
| E5 | Single-allocation XML text decode; shared error-code strings | allocation |
| E6 | Vectorized end-tag search for skipped rows and cells | CPU |
| E7 | One tag walk over post-sheetData metadata (removes a quadratic cost) | CPU |
| E8–E10 | Layout: indexed and incremental algorithms instead of all-pairs scans | CPU |
| E11 | Analysis sink decodes shared strings only for block-start spans | allocation |
| E13 | Strict single-pass fast path for common `<c>` attribute shapes | CPU |
| E14 | Release/acquire publication in `SharedStringTable` | correctness |
| E15 | Disposed workbook releases the SST and analyzer metadata | retained memory |
| E16 | XLSB cursor cell buffer grows on demand; default input window | allocation |
| E17 | SST parser start deferred to the first lookup | allocation |
| E18 | File-backed async reads complete inline when no sync context is current | async CPU and allocation |
| E19 | Layout: column interval index, sliced components, ordered merge search | CPU |

Every kept change passed four gates:
- the full test suite, ending at 1029/1029 (958 at baseline, plus 71 new tests);
- for changes that can affect output, `eqcheck.sh`, a full JSON dump of `AnalyzeLayout` and
  `AnalyzeSheet(Observed/Full)` over 19 workbooks, which came out identical;
- a paired A/B, or for memory-only changes a deterministic allocation or retention probe;
- an entry in `EXPERIMENTS.md`.

The public API, file formats, cancellation behaviour, concurrency model and AOT compatibility are
unchanged, and no dependencies were added. Three behaviour changes are deliberate:

- **E15:** an operation still running when its workbook is disposed now gets
  `ObjectDisposedException` on its next shared-string access. Before, it silently got `""`.
  Cursors opened before Dispose keep working as before.
- **E17:** a corrupt shared-strings header now fails at the first shared-string lookup, not
  when the cursor opens. A sheet with no `t="s"` cells no longer fails at all.
- **E18:** with no `SynchronizationContext` or custom scheduler, a file-backed async read
  runs the blocking `pread` on the calling thread instead of a pool thread. The total blocking
  is the same. Callers on a UI or single-threaded context keep the old offload.

## 3. Rejected and deferred

- **E12, rejected:** cache a shared string only on its second lookup (TinyLFU doorkeeper).
  Retained memory fell 58 %, but peak RSS rose 12 %, a second scan allocated 5.5× more, and the
  identity contract of `TryGetSharedStringId` was weakened.
- **E20, deferred:** a HEADER AUTO resolver built on a dedicated early-stopping sink
  (−29 % on t60k HEADER AUTO queries, identical over 51,768 queries). It duplicates 386 lines of
  AnalysisSink's region heuristics and drops telemetry and corrupt-file errors. Do it instead as
  a shared segmenter with an early-stop hook.
- **Worker-level rejections:**
  - `FileOptions.Asynchronous` gives nothing on Linux: same strategy, same pool path.
  - A 64 KB FileStream buffer was the fallback for E18, but undoes part of E2.
  - Lazy SST arena allocation without deferring the start was made unnecessary by E17.
  - Sizing the XLSB cell buffer from the dimension record: growing on demand is exact.
  - Expression binding in Query: its ceiling was under 10 % of a raw scan, so it was not
    prototyped.
  - Partitioned or linear-probing aggregate hash tables (DuckDB-style): hashing is 1–2 % of query
    time and groups are capped at 10K.
  - Learned table detection (TableSense): it needs ML dependencies and conflicts with AOT.

## 4. Research consulted (dated primary sources)

| Source | Used for |
|---|---|
| Clinger, "How to read floating point numbers accurately", PLDI 1990, doi:10.1145/93542.93557 | E4 exactness argument |
| Lemire, "Number Parsing at a Gigabyte per Second", SP&E 51(8) 2021, arXiv:2101.11408 (§3) | E4 fast-path bounds |
| dotnet/runtime PR #62301 (merged 2022-02-08) | `Utf8Parser` already has Eisel–Lemire; short-input cost is pipeline overhead |
| dotnet/runtime issue #101465 / PR #102403 (closed 2024-09-19) | zlib-ng vs system zlib on Linux; inflate shares are upper bounds here |
| dotnet/runtime release/10.0 `RandomAccess.Unix.cs`, `SafeFileHandle.ThreadPoolValueTaskSource.cs`, `FileStreamHelpers.Unix.cs`, `DeflateStream.cs` (read 2026-10-26) | E18 mechanism and cancellation semantics |
| Sitnik, "File IO improvements in .NET 6", .NET Blog, 2021 | E18 |
| dotnet/runtime `docs/design/specs/Memory-model.md` (read 2026-10-27) | E14 |
| dotnet/runtime `SharedArrayPool.cs` (main, read 2026-10-27) | E16 pool-retention reasoning |
| Einziger, Friedman, Manes, "TinyLFU", ACM TOS 13(4) 2017, arXiv:1512.00727 | E12 (rejected) |
| Langdale & Lemire, "Parsing Gigabytes of JSON per Second", VLDB J. 2019 | Follow-up idea: a per-window structural bitmask |
| Kraft, Gienieczko, Giceva, "Streaming with a Touch of DOM", 2026, doi:10.1145/3789237.3809120 | Follow-up idea: row checkpoints (blocked by `DeflateStream` state) |
| Knespel & Brunst, "Rapidgzip", HPDC 2023, arXiv:2308.08955; Medforth et al., "icXML", Balisage 2013 | Follow-up idea: an inflate/parse pipeline |
| Neumann & Freitag, Umbra, CIDR 2020; Arrow Utf8View | Internal prefixed string views for Query predicates (inference only) |
| SpreadsheetLLM, arXiv:2407.09025 (2024) | Supports confining per-cell work to block-start rows (E11) |
| calamine `src/xlsx/mod.rs`; Apache POI SXSSF / Bugzilla 53130 | Prior art for SST storage and inline strings |

Applying a published result to XLSight is marked as inference in `EXPERIMENTS.md` wherever it was
not measured.

## 5. Benchmark harness (FastBench)

`benchmarks/XLSight.FastBench/` contains the following (see `README.md`):
- **Manifest:** frozen v1, 29 default cases, 7 confirm cases and 2 probes.
- **Fixtures:** deterministic generators with fixed ZIP timestamps, pinned by SHA-256.
- **Validation:** cheap and deep per-case digests, pinned in the manifest.
- **Deadlines:**
  - an internal suite deadline of 24 s;
  - a hard-deadline timer that exits with code 4;
  - an external `timeout 30s` that kills the process group and writes a stub JSON.
- **Isolated cold cases:** run in child processes and report peak RSS (VmHWM).
- **Metrics:** allocation per op, GC counts and peak RSS.
- **Output:** JSON, Markdown and `latest.json`.
- **`compare`:** paired or unpaired.
- **`ab`:** interleaved paired A/B against any git ref.
- **`selftest`:** re-run at the end of the campaign. All six deadline and failure paths behaved as
  specified: incomplete exit 3, hard deadline exit 4, external kill exit 5, missing fixture
  exit 2, stale binary exit 1, and no leftover processes.

## 6. Open issues

1. **`scan.strings_xlsb`** was +12.8 % cumulative (inconclusive, ±3.7 % noise) on a 1.2 ms
   operation. E16 alone was +3.7 % against ±5.6 % noise over 9 rounds, so it is not the cause. To
   bisect: run `ab` over E15 and E17 with the `xlsb` filter on a quiet host.
2. **Readers used after Dispose:** an XLSX reader, or an XLSB reader opened from a stream, still
   returns `""` for shared-string entries not parsed before Dispose. An XLSB reader opened from a
   file throws `NotSupportedException`. Recommended fix: reference-count the table so it is
   released when the last cursor closes, or throw `ObjectDisposedException`.
3. **`TryGetSharedStringId` documentation** promises the identical string object. That holds for
   indexes below the cache length (131,072) but not above it. Fix the wording or the behaviour.
4. **Test debt:** a helper near line 1036 of `XlsxSheetScannerTests.cs` disposes the stream it
   hands to the SST. That is safe only because its fixtures have no SST, and with E17 a fixture
   that has one would fail lazily.
5. **The host has no zlib-ng:** inflate-bound shares measured here are upper bounds for Windows
   and macOS builds that ship zlib-ng.

## 7. Ranked follow-ups

1. **Shared region segmenter with an early-stop hook, then HEADER AUTO on it (E20).** Expected
   −29 % on HEADER AUTO queries. The acceptance harness (`w2-query/qeq.sh`) already exists.
2. **Layout `FindDenseFields` (~103 ms) and `GetAxes` (~54 ms) on mixed_sparse.** These are the
   next hot phases after E19.
3. **Reference-counted SST lifetime (open issue 2).** It fixes silent `""` and keeps the E15
   retention win.
4. **Structural bitmask per scan window** (Langdale & Lemire) to replace per-tag `IndexOf`. Only
   worth it after re-profiling on a zlib-ng runtime.
5. **Overlapping inflate with parse** (producer/consumer) for large sheets. It needs care with
   cancellation and the single-reader concurrency contract.
