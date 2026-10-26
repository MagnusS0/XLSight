base: ab-base-1 rev 2f7d50f1757a dirty clean-base-worktree XLSight.dll 448d8a428ab4 (complete, 19395 ms); ab-base-2 rev 2f7d50f1757a dirty clean-base-worktree XLSight.dll 448d8a428ab4 (complete, 19898 ms); ab-base-3 rev 2f7d50f1757a dirty clean-base-worktree XLSight.dll 448d8a428ab4 (complete, 20335 ms)
cand: ab-cand-1 rev 2f7d50f1757a dirty src-diff-717ef5d4abdd XLSight.dll 80d33c1aeec1 (complete, 19534 ms); ab-cand-2 rev 2f7d50f1757a dirty src-diff-717ef5d4abdd XLSight.dll 80d33c1aeec1 (complete, 19928 ms); ab-cand-3 rev 2f7d50f1757a dirty src-diff-717ef5d4abdd XLSight.dll 80d33c1aeec1 (complete, 19349 ms)
mode: paired interleaved rounds (median of per-round deltas; verdict needs every round to agree in sign)

| case | base ms | cand ms | Δ time | noise ± | verdict | base alloc/op | cand alloc/op | Δ alloc |
|---|--:|--:|--:|--:|---|--:|--:|--:|
| mem.cold_scan_strings_unique | 308.235 | 324.805 | +5.4% | 3.0% | single-op (see RSS) | 20.1 MiB | 20.1 MiB | +0.0% |
| mem.cold_scan_strings_unique peak RSS | 75.4 MiB | 75.2 MiB | -0.4% | | | | | |
| mem.cold_analyze_banking_real | 340.658 | 333.427 | -2.1% | 3.0% | single-op (see RSS) | 4.4 MiB | 4.4 MiB | +0.0% |
| mem.cold_analyze_banking_real peak RSS | 63.8 MiB | 63.8 MiB | +0.0% | | | | | |
| open.small | 0.152 | 0.197 | +30.5% | 45.3% | ~ inconclusive | 53.9 KiB | 53.9 KiB | +0.0% |
| open.banking_real | 6.550 | 6.711 | +2.4% | 7.3% | ~ inconclusive | 594.2 KiB | 594.2 KiB | -+0.0% |
| readcell.large_early_exit | 0.551 | 0.551 | -0.7% | 5.3% | ~ inconclusive | 87.1 KiB | 87.1 KiB | +0.0% |
| scan.numeric_xlsx | 133.495 | 157.475 | +15.9% | 19.9% | ~ inconclusive | 88.1 KiB | 88.1 KiB | +0.0% |
| scan.numeric_xlsb | 46.544 | 57.455 | +9.2% | 61.0% | ~ inconclusive | 28.6 KiB | 28.6 KiB | +0.0% |
| scan.strings_xlsb | 1.145 | 1.121 | +3.1% | 21.2% | ~ inconclusive | 54.7 KiB | 54.7 KiB | +0.0% |
| scan.strings_repeated | 112.546 | 111.883 | +5.9% | 10.2% | ~ inconclusive | 306.2 KiB | 306.1 KiB | -+0.0% |
| scan.strings_unique | 108.289 | 113.880 | +5.6% | 3.0% | SLOWER | 19.7 MiB | 19.7 MiB | +0.0% |
| scan.mixed_sparse_values | 117.117 | 126.325 | +7.9% | 12.4% | ~ inconclusive | 2.6 MiB | 2.6 MiB | +0.0% |
| scan.mixed_sparse_formulas | 157.920 | 152.126 | +1.0% | 19.7% | ~ inconclusive | 5.1 MiB | 5.1 MiB | +0.0% |
| stream.snapshots_mixed | 127.711 | 127.471 | +2.0% | 3.4% | ~ inconclusive | 16.5 MiB | 16.5 MiB | -+0.0% |
| scan.real_mcdonalds_all | 97.680 | 94.731 | -4.0% | 3.4% | ~ inconclusive | 764.1 KiB | 764.1 KiB | +0.0% |
| range.large_middle_rows | 35.980 | 35.056 | -0.3% | 4.5% | ~ inconclusive | 87.6 KiB | 87.6 KiB | +0.0% |
| range.readrange_medium | 4.478 | 4.561 | +2.3% | 20.9% | ~ inconclusive | 457.0 KiB | 457.0 KiB | +0.0% |
| range.async_large_middle_rows | 40.438 | 39.368 | -7.3% | 6.8% | ~ inconclusive | 201.6 KiB | 196.5 KiB | -2.5% |
| analyze.full_banking_real | 80.435 | 83.124 | +3.5% | 13.9% | ~ inconclusive | 3.7 MiB | 3.8 MiB | +2.9% |
| analyze.observed_strings_repeated | 225.247 | 210.106 | -0.1% | 21.8% | ~ inconclusive | 596.9 KiB | 596.9 KiB | +0.0% |
| analyze.full_merged_regions | 176.501 | 7.346 | -96.5% | 9.5% | faster | 480.7 KiB | 480.0 KiB | -0.1% |
| layout.complex_calculator | 1.363 | 1.344 | +1.6% | 9.7% | ~ inconclusive | 291.7 KiB | 291.8 KiB | +0.0% |
| layout.mcdonalds_all | 187.916 | 181.581 | -3.4% | 24.6% | ~ inconclusive | 6.0 MiB | 6.0 MiB | +0.0% |
| layout.sparse_fragmented_2k | 108.198 | 112.543 | +3.3% | 5.3% | ~ inconclusive | 6.2 MiB | 6.2 MiB | -0.4% |
| query.builder_filter_sum_large | 130.295 | 128.193 | -1.6% | 21.4% | ~ inconclusive | 93.2 KiB | 93.2 KiB | +0.0% |
| query.groupby_category | 121.670 | 114.885 | -5.6% | 35.5% | ~ inconclusive | 218.0 KiB | 218.0 KiB | +0.0% |
| query.expr_groupby_orderby_limit | 125.626 | 125.156 | -4.7% | 8.5% | ~ inconclusive | 755.3 KiB | 755.3 KiB | +0.0% |
| query.rows_orderby_limit | 107.307 | 103.598 | -3.8% | 11.0% | ~ inconclusive | 328.4 KiB | 328.2 KiB | -0.1% |
| query.rows_limit_early_exit | 1.385 | 1.345 | -2.9% | 4.7% | ~ inconclusive | 239.3 KiB | 239.3 KiB | -+0.0% |
| query.distinct_product | 112.904 | 114.575 | +0.6% | 6.5% | ~ inconclusive | 735.1 KiB | 735.1 KiB | +0.0% |
