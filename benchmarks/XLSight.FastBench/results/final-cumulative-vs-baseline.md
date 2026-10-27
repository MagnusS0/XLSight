base: ab-base-1 rev 5643a0cc2dad dirty clean-base-worktree XLSight.dll ddedb92cd694 (complete, 19860 ms); ab-base-2 rev 5643a0cc2dad dirty clean-base-worktree XLSight.dll ddedb92cd694 (complete, 19630 ms); ab-base-3 rev 5643a0cc2dad dirty clean-base-worktree XLSight.dll ddedb92cd694 (complete, 19576 ms); ab-base-4 rev 5643a0cc2dad dirty clean-base-worktree XLSight.dll ddedb92cd694 (complete, 19552 ms); ab-base-5 rev 5643a0cc2dad dirty clean-base-worktree XLSight.dll ddedb92cd694 (complete, 19455 ms)
cand: ab-cand-1 rev afe3c50d4fb5 dirty clean XLSight.dll afcc12f9b82f (complete, 18194 ms); ab-cand-2 rev afe3c50d4fb5 dirty clean XLSight.dll afcc12f9b82f (complete, 17979 ms); ab-cand-3 rev afe3c50d4fb5 dirty clean XLSight.dll afcc12f9b82f (complete, 18240 ms); ab-cand-4 rev afe3c50d4fb5 dirty clean XLSight.dll afcc12f9b82f (complete, 18107 ms); ab-cand-5 rev afe3c50d4fb5 dirty clean XLSight.dll afcc12f9b82f (complete, 18184 ms)
mode: paired interleaved rounds (median of per-round deltas; verdict needs every round to agree in sign)

| case | base ms | cand ms | Δ time | noise ± | verdict | base alloc/op | cand alloc/op | Δ alloc |
|---|--:|--:|--:|--:|---|--:|--:|--:|
| mem.cold_scan_strings_unique | 336.163 | 315.719 | -6.1% | 3.0% | single-op (see RSS) | 20.3 MiB | 20.1 MiB | -0.9% |
| mem.cold_scan_strings_unique peak RSS | 74.8 MiB | 74.6 MiB | -0.3% | | | | | |
| mem.cold_analyze_banking_real | 260.313 | 213.534 | -18.0% | 3.0% | single-op (see RSS) | 5.3 MiB | 4.4 MiB | -16.8% |
| mem.cold_analyze_banking_real peak RSS | 63.5 MiB | 63.9 MiB | +0.6% | | | | | |
| open.small | 0.174 | 0.143 | -16.6% | 11.0% | ~ inconclusive | 182.0 KiB | 53.9 KiB | -70.4% |
| open.banking_real | 6.333 | 6.211 | -2.1% | 3.8% | ~ inconclusive | 658.3 KiB | 594.2 KiB | -9.7% |
| readcell.large_early_exit | 0.590 | 0.521 | -11.4% | 4.0% | faster | 279.2 KiB | 87.1 KiB | -68.8% |
| scan.numeric_xlsx | 160.351 | 114.536 | -25.0% | 8.2% | faster | 280.2 KiB | 88.1 KiB | -68.6% |
| scan.numeric_xlsb | 47.795 | 49.796 | +0.1% | 26.7% | ~ inconclusive | 28.6 KiB | 28.6 KiB | +0.0% |
| scan.strings_xlsb | 1.159 | 1.223 | +12.8% | 3.7% | ~ inconclusive | 54.7 KiB | 54.7 KiB | +0.0% |
| scan.strings_repeated | 114.811 | 91.595 | -20.7% | 4.1% | faster | 506.2 KiB | 306.1 KiB | -39.5% |
| scan.strings_unique | 107.042 | 99.555 | -7.5% | 5.0% | faster | 19.9 MiB | 19.7 MiB | -1.0% |
| scan.mixed_sparse_values | 124.973 | 108.380 | -13.4% | 5.1% | faster | 4.6 MiB | 2.6 MiB | -43.8% |
| scan.mixed_sparse_formulas | 147.909 | 139.165 | -6.6% | 6.7% | ~ inconclusive | 9.3 MiB | 5.1 MiB | -45.1% |
| stream.snapshots_mixed | 126.127 | 119.911 | -4.6% | 7.4% | ~ inconclusive | 18.5 MiB | 16.5 MiB | -10.8% |
| scan.real_mcdonalds_all | 95.581 | 76.482 | -19.8% | 5.6% | faster | 844.1 KiB | 764.1 KiB | -9.5% |
| range.large_middle_rows | 49.026 | 33.696 | -31.4% | 3.0% | faster | 279.7 KiB | 87.6 KiB | -68.7% |
| range.readrange_medium | 4.540 | 3.961 | -14.1% | 17.3% | ~ inconclusive | 649.0 KiB | 457.0 KiB | -29.6% |
| range.async_large_middle_rows | 118.995 | 30.787 | -74.5% | 4.2% | faster | 12.0 MiB | 108.5 KiB | -99.1% |
| analyze.full_banking_real | 74.355 | 66.527 | -10.5% | 7.1% | faster | 4.6 MiB | 3.8 MiB | -17.1% |
| analyze.observed_strings_repeated | 189.974 | 163.356 | -11.6% | 11.8% | ~ inconclusive | 860.9 KiB | 469.6 KiB | -45.5% |
| analyze.full_merged_regions | 198.882 | 5.209 | -97.3% | 3.0% | faster | 744.8 KiB | 477.9 KiB | -35.8% |
| layout.complex_calculator | 1.381 | 1.161 | -15.6% | 5.4% | faster | 481.6 KiB | 289.5 KiB | -39.9% |
| layout.mcdonalds_all | 167.378 | 143.146 | -17.1% | 17.2% | ~ inconclusive | 6.0 MiB | 5.6 MiB | -7.9% |
| layout.sparse_fragmented_2k | 98.652 | 31.988 | -67.4% | 6.2% | faster | 6.6 MiB | 5.3 MiB | -19.8% |
| query.builder_filter_sum_large | 143.130 | 108.200 | -23.0% | 11.2% | faster | 285.4 KiB | 93.2 KiB | -67.3% |
| query.groupby_category | 115.318 | 89.489 | -23.2% | 6.0% | faster | 418.1 KiB | 217.9 KiB | -47.9% |
| query.expr_groupby_orderby_limit | 128.940 | 100.751 | -21.8% | 6.1% | faster | 955.4 KiB | 755.3 KiB | -20.9% |
| query.rows_orderby_limit | 114.654 | 87.850 | -23.1% | 3.0% | faster | 528.4 KiB | 328.4 KiB | -37.9% |
| query.rows_limit_early_exit | 1.452 | 1.257 | -14.4% | 4.5% | faster | 439.3 KiB | 239.4 KiB | -45.5% |
| query.distinct_product | 115.726 | 87.188 | -24.5% | 3.3% | faster | 935.1 KiB | 734.9 KiB | -21.4% |
