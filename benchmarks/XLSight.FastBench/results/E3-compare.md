base: ab-base-1 rev a6c45e140299 dirty clean-base-worktree XLSight.dll ffc53111ed68 (complete, 20433 ms); ab-base-2 rev a6c45e140299 dirty clean-base-worktree XLSight.dll ffc53111ed68 (complete, 20362 ms); ab-base-3 rev a6c45e140299 dirty clean-base-worktree XLSight.dll ffc53111ed68 (complete, 21026 ms)
cand: ab-cand-1 rev a6c45e140299 dirty src-diff-e7a42fe51c87 XLSight.dll 61cc56a1c236 (complete, 19991 ms); ab-cand-2 rev a6c45e140299 dirty src-diff-e7a42fe51c87 XLSight.dll 61cc56a1c236 (complete, 20146 ms); ab-cand-3 rev a6c45e140299 dirty src-diff-e7a42fe51c87 XLSight.dll 61cc56a1c236 (complete, 20467 ms)
mode: paired interleaved rounds (median of per-round deltas; verdict needs every round to agree in sign)

| case | base ms | cand ms | Δ time | noise ± | verdict | base alloc/op | cand alloc/op | Δ alloc |
|---|--:|--:|--:|--:|---|--:|--:|--:|
| mem.cold_scan_strings_unique | 311.126 | 277.524 | -10.8% | 3.0% | single-op (see RSS) | 20.1 MiB | 20.1 MiB | +0.0% |
| mem.cold_scan_strings_unique peak RSS | 75.2 MiB | 75.3 MiB | +0.2% | | | | | |
| mem.cold_analyze_banking_real | 342.880 | 333.276 | -2.8% | 3.0% | single-op (see RSS) | 4.4 MiB | 4.4 MiB | +0.5% |
| mem.cold_analyze_banking_real peak RSS | 63.3 MiB | 63.3 MiB | +0.1% | | | | | |
| open.small | 0.159 | 0.147 | -10.3% | 22.3% | ~ inconclusive | 53.9 KiB | 53.9 KiB | +0.0% |
| open.banking_real | 6.699 | 6.428 | -4.6% | 10.1% | ~ inconclusive | 594.2 KiB | 594.2 KiB | -+0.0% |
| readcell.large_early_exit | 0.571 | 0.555 | -1.5% | 4.9% | ~ inconclusive | 87.1 KiB | 87.1 KiB | -+0.0% |
| scan.numeric_xlsx | 161.747 | 173.542 | +2.8% | 30.8% | ~ inconclusive | 88.1 KiB | 88.1 KiB | +0.0% |
| scan.numeric_xlsb | 59.612 | 47.353 | -8.6% | 22.1% | ~ inconclusive | 28.6 KiB | 28.6 KiB | +0.0% |
| scan.strings_xlsb | 1.268 | 1.141 | -10.1% | 39.7% | ~ inconclusive | 54.7 KiB | 54.7 KiB | +0.0% |
| scan.strings_repeated | 134.360 | 118.513 | -4.7% | 15.1% | ~ inconclusive | 314.2 KiB | 306.2 KiB | -2.5% |
| scan.strings_unique | 112.438 | 116.654 | +1.2% | 16.0% | ~ inconclusive | 19.7 MiB | 19.7 MiB | -+0.0% |
| scan.mixed_sparse_values | 132.266 | 133.527 | +1.0% | 6.3% | ~ inconclusive | 4.4 MiB | 4.4 MiB | -0.2% |
| scan.mixed_sparse_formulas | 163.780 | 155.958 | -2.1% | 4.5% | ~ inconclusive | 9.1 MiB | 9.1 MiB | -0.1% |
| stream.snapshots_mixed | 128.800 | 135.080 | -1.3% | 42.5% | ~ inconclusive | 18.3 MiB | 18.3 MiB | -+0.0% |
| scan.real_mcdonalds_all | 102.695 | 99.055 | -1.3% | 8.2% | ~ inconclusive | 780.1 KiB | 772.2 KiB | -1.0% |
| range.large_middle_rows | 49.965 | 49.879 | -1.0% | 3.0% | ~ inconclusive | 87.7 KiB | 87.7 KiB | +0.0% |
| range.readrange_medium | 4.603 | 4.945 | +6.8% | 37.9% | ~ inconclusive | 457.0 KiB | 457.0 KiB | +0.0% |
| range.async_large_middle_rows | 53.662 | 58.671 | +9.3% | 15.2% | ~ inconclusive | 206.0 KiB | 200.2 KiB | -2.8% |
| analyze.full_banking_real | 83.579 | 78.642 | -5.9% | 7.4% | ~ inconclusive | 3.7 MiB | 3.7 MiB | +0.3% |
| analyze.observed_strings_repeated | 203.601 | 187.378 | -8.9% | 15.3% | ~ inconclusive | 604.8 KiB | 596.8 KiB | -1.3% |
| analyze.full_merged_regions | 210.082 | 175.738 | -16.3% | 9.2% | ~ inconclusive | 488.7 KiB | 480.7 KiB | -1.6% |
| layout.complex_calculator | 1.398 | 1.395 | -4.4% | 20.4% | ~ inconclusive | 299.6 KiB | 291.5 KiB | -2.7% |
| layout.mcdonalds_all | 189.286 | 169.843 | -8.1% | 12.3% | ~ inconclusive | 6.0 MiB | 6.0 MiB | +0.1% |
| layout.sparse_fragmented_2k | 103.251 | 100.481 | -2.7% | 4.2% | ~ inconclusive | 6.4 MiB | 6.4 MiB | -0.1% |
| query.builder_filter_sum_large | 160.688 | 155.302 | +3.1% | 6.9% | ~ inconclusive | 93.3 KiB | 93.3 KiB | +0.0% |
| query.groupby_category | 129.457 | 118.603 | -4.9% | 4.0% | faster | 226.0 KiB | 218.0 KiB | -3.5% |
| query.expr_groupby_orderby_limit | 137.251 | 134.391 | -2.1% | 10.1% | ~ inconclusive | 763.3 KiB | 755.4 KiB | -1.0% |
| query.rows_orderby_limit | 121.540 | 117.014 | -3.7% | 3.0% | ~ inconclusive | 336.4 KiB | 328.4 KiB | -2.4% |
| query.rows_limit_early_exit | 1.482 | 1.392 | +0.3% | 18.7% | ~ inconclusive | 247.3 KiB | 239.3 KiB | -3.2% |
| query.distinct_product | 127.218 | 125.927 | +1.6% | 4.9% | ~ inconclusive | 743.0 KiB | 735.1 KiB | -1.1% |
