base: ab-base-1 rev 5643a0cc2dad dirty clean-base-worktree XLSight.dll ddedb92cd694 (complete, 21631 ms); ab-base-2 rev 5643a0cc2dad dirty clean-base-worktree XLSight.dll ddedb92cd694 (incomplete, 22805 ms); ab-base-3 rev 5643a0cc2dad dirty clean-base-worktree XLSight.dll ddedb92cd694 (complete, 21131 ms)
cand: ab-cand-1 rev 937d7d8dd073 dirty clean XLSight.dll 72685d6f96d7 (complete, 19749 ms); ab-cand-2 rev 937d7d8dd073 dirty clean XLSight.dll 72685d6f96d7 (complete, 20529 ms); ab-cand-3 rev 937d7d8dd073 dirty clean XLSight.dll 72685d6f96d7 (complete, 19962 ms)
mode: paired interleaved rounds (median of per-round deltas; verdict needs every round to agree in sign)

| case | base ms | cand ms | Δ time | noise ± | verdict | base alloc/op | cand alloc/op | Δ alloc |
|---|--:|--:|--:|--:|---|--:|--:|--:|
| mem.cold_scan_strings_unique | 296.130 | 344.532 | +16.3% | 3.0% | single-op (see RSS) | 20.3 MiB | 20.1 MiB | -0.9% |
| mem.cold_scan_strings_unique peak RSS | 75.7 MiB | 75.2 MiB | -0.5% | | | | | |
| mem.cold_analyze_banking_real | 427.842 | 603.541 | +41.1% | 3.0% | single-op (see RSS) | 5.3 MiB | 4.4 MiB | -16.9% |
| mem.cold_analyze_banking_real peak RSS | 64.2 MiB | 64.5 MiB | +0.6% | | | | | |
| open.small | 0.185 | 0.187 | +1.4% | 40.6% | ~ inconclusive | 182.0 KiB | 53.9 KiB | -70.4% |
| open.banking_real | 6.610 | 6.820 | +3.2% | 15.2% | ~ inconclusive | 658.3 KiB | 594.2 KiB | -9.7% |
| readcell.large_early_exit | 0.682 | 0.613 | -5.2% | 14.9% | ~ inconclusive | 279.2 KiB | 87.1 KiB | -68.8% |
| scan.numeric_xlsx | 166.958 | 152.184 | -18.2% | 17.0% | ~ inconclusive | 280.1 KiB | 88.1 KiB | -68.6% |
| scan.numeric_xlsb | 67.241 | 53.750 | -20.1% | 46.8% | ~ inconclusive | 28.2 KiB | 28.6 KiB | +1.1% |
| scan.strings_xlsb | 1.286 | 1.200 | -7.5% | 14.1% | ~ inconclusive | 54.7 KiB | 54.7 KiB | +0.0% |
| scan.strings_repeated | 121.831 | 125.956 | -13.7% | 13.1% | ~ inconclusive | 506.3 KiB | 306.2 KiB | -39.5% |
| scan.strings_unique | 117.340 | 118.271 | +0.8% | 32.8% | ~ inconclusive | 19.9 MiB | 19.7 MiB | -1.0% |
| scan.mixed_sparse_values | 140.200 | 126.692 | -12.3% | 9.9% | faster | 4.6 MiB | 2.6 MiB | -43.8% |
| scan.mixed_sparse_formulas | 153.728 | 152.123 | -0.9% | 11.9% | ~ inconclusive | 9.3 MiB | 5.1 MiB | -45.1% |
| stream.snapshots_mixed | 129.199 | 130.720 | +1.6% | 13.0% | ~ inconclusive | 18.5 MiB | 16.5 MiB | -10.8% |
| scan.real_mcdonalds_all | 97.143 | 97.976 | +0.1% | 6.7% | ~ inconclusive | 844.1 KiB | 764.1 KiB | -9.5% |
| range.large_middle_rows | 50.509 | 35.165 | -30.8% | 3.2% | faster | 279.8 KiB | 87.6 KiB | -68.7% |
| range.readrange_medium | 5.768 | 4.591 | -20.7% | 15.9% | faster | 649.1 KiB | 457.0 KiB | -29.6% |
| range.async_large_middle_rows | 136.474 | 42.816 | -69.1% | 9.9% | faster | 12.9 MiB | 197.6 KiB | -98.5% |
| analyze.full_banking_real | 92.441 | 96.492 | +6.6% | 98.6% | ~ inconclusive | 4.6 MiB | 3.7 MiB | -19.4% |
| analyze.observed_strings_repeated | 232.137 | 221.593 | -5.6% | 23.0% | ~ inconclusive | 860.3 KiB | 469.6 KiB | -45.4% |
| analyze.full_merged_regions | 197.693 | 5.699 | -97.0% | 7.8% | faster | 744.1 KiB | 477.9 KiB | -35.8% |
| layout.complex_calculator | 1.502 | 1.232 | -17.3% | 9.9% | ~ inconclusive | 481.6 KiB | 288.5 KiB | -40.1% |
| layout.mcdonalds_all | 201.945 | 205.300 | -3.4% | 50.6% | ~ inconclusive | 6.0 MiB | 5.5 MiB | -8.2% |
| layout.sparse_fragmented_2k | 106.389 | 35.072 | -67.2% | 11.0% | faster | 6.6 MiB | 5.2 MiB | -20.2% |
| query.builder_filter_sum_large | 152.551 | 135.568 | -8.3% | 9.3% | ~ inconclusive | 285.4 KiB | 93.4 KiB | -67.3% |
| query.groupby_category | 127.629 | 127.162 | -0.4% | 27.4% | ~ inconclusive | 418.1 KiB | 218.1 KiB | -47.8% |
| query.expr_groupby_orderby_limit | 155.357 | 127.355 | -18.0% | 14.3% | faster | 955.4 KiB | 754.7 KiB | -21.0% |
| query.rows_orderby_limit | 120.453 | 106.074 | -11.9% | 4.8% | faster | 528.4 KiB | 328.3 KiB | -37.9% |
| query.rows_limit_early_exit | 1.471 | 1.582 | +7.1% | 35.0% | ~ inconclusive | 439.3 KiB | 239.3 KiB | -45.5% |
| query.distinct_product | 128.233 | 104.708 | -18.3% | 9.9% | faster? (unpaired; use ab) | 935.1 KiB | 735.0 KiB | -21.4% |
