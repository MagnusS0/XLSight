base: ab-base-1 rev 5097f4cb6a8f dirty clean-base-worktree XLSight.dll 15bcb7da7bd7 (complete, 20012 ms); ab-base-2 rev 5097f4cb6a8f dirty clean-base-worktree XLSight.dll 15bcb7da7bd7 (complete, 19809 ms); ab-base-3 rev 5097f4cb6a8f dirty clean-base-worktree XLSight.dll 15bcb7da7bd7 (complete, 20630 ms)
cand: ab-cand-1 rev 5097f4cb6a8f dirty src-diff-f138c3ad07f1 XLSight.dll eec32f0bdf7a (complete, 20129 ms); ab-cand-2 rev 5097f4cb6a8f dirty src-diff-f138c3ad07f1 XLSight.dll eec32f0bdf7a (complete, 20098 ms); ab-cand-3 rev 5097f4cb6a8f dirty src-diff-f138c3ad07f1 XLSight.dll eec32f0bdf7a (complete, 20211 ms)
mode: paired interleaved rounds (median of per-round deltas; verdict needs every round to agree in sign)

| case | base ms | cand ms | Δ time | noise ± | verdict | base alloc/op | cand alloc/op | Δ alloc |
|---|--:|--:|--:|--:|---|--:|--:|--:|
| mem.cold_scan_strings_unique | 297.066 | 280.139 | -5.7% | 3.0% | single-op (see RSS) | 20.3 MiB | 20.3 MiB | +0.0% |
| mem.cold_scan_strings_unique peak RSS | 75.6 MiB | 75.5 MiB | -0.1% | | | | | |
| mem.cold_analyze_banking_real | 299.597 | 317.339 | +5.9% | 3.0% | single-op (see RSS) | 5.3 MiB | 5.3 MiB | -0.1% |
| mem.cold_analyze_banking_real peak RSS | 64.2 MiB | 64.1 MiB | -0.1% | | | | | |
| open.small | 0.185 | 0.190 | +4.2% | 13.6% | ~ inconclusive | 182.0 KiB | 182.0 KiB | +0.0% |
| open.banking_real | 6.763 | 6.897 | +0.1% | 11.5% | ~ inconclusive | 658.3 KiB | 658.3 KiB | -+0.0% |
| readcell.large_early_exit | 0.625 | 0.630 | -0.9% | 16.5% | ~ inconclusive | 279.2 KiB | 279.2 KiB | +0.0% |
| scan.numeric_xlsx | 149.935 | 157.349 | +8.7% | 12.1% | ~ inconclusive | 280.2 KiB | 280.1 KiB | -+0.0% |
| scan.numeric_xlsb | 46.176 | 47.296 | -0.8% | 9.6% | ~ inconclusive | 28.5 KiB | 28.6 KiB | +0.2% |
| scan.strings_xlsb | 1.129 | 1.125 | -0.1% | 3.1% | ~ inconclusive | 54.7 KiB | 54.7 KiB | +0.0% |
| scan.strings_repeated | 116.806 | 117.756 | +0.6% | 3.0% | ~ inconclusive | 506.2 KiB | 506.2 KiB | -+0.0% |
| scan.strings_unique | 108.466 | 110.157 | +2.3% | 8.8% | ~ inconclusive | 19.9 MiB | 19.9 MiB | +0.0% |
| scan.mixed_sparse_values | 127.540 | 127.512 | -0.9% | 8.6% | ~ inconclusive | 4.6 MiB | 4.6 MiB | +0.0% |
| scan.mixed_sparse_formulas | 151.598 | 150.044 | -1.0% | 3.4% | ~ inconclusive | 9.3 MiB | 9.3 MiB | +0.0% |
| stream.snapshots_mixed | 128.560 | 132.585 | +4.6% | 3.0% | ~ inconclusive | 18.5 MiB | 18.5 MiB | +0.0% |
| scan.real_mcdonalds_all | 96.343 | 99.547 | +3.1% | 24.8% | ~ inconclusive | 844.1 KiB | 844.1 KiB | +0.0% |
| range.large_middle_rows | 50.493 | 52.640 | +4.3% | 13.0% | ~ inconclusive | 279.7 KiB | 279.7 KiB | +0.0% |
| range.readrange_medium | 5.047 | 4.833 | -2.4% | 15.4% | ~ inconclusive | 649.0 KiB | 649.0 KiB | +0.0% |
| range.async_large_middle_rows | 134.730 | 58.822 | -57.4% | 10.9% | faster | 13.9 MiB | 392.2 KiB | -97.2% |
| analyze.full_banking_real | 83.644 | 88.091 | +3.1% | 13.1% | ~ inconclusive | 4.6 MiB | 4.6 MiB | +0.0% |
| analyze.observed_strings_repeated | 186.033 | 206.766 | +9.5% | 21.1% | ~ inconclusive | 860.9 KiB | 860.9 KiB | +0.0% |
| analyze.full_merged_regions | 179.158 | 178.997 | -1.4% | 3.0% | ~ inconclusive | 744.7 KiB | 744.8 KiB | +0.0% |
| layout.complex_calculator | 1.380 | 1.397 | +1.4% | 4.2% | ~ inconclusive | 481.5 KiB | 481.6 KiB | +0.0% |
| layout.mcdonalds_all | 174.608 | 186.272 | +6.7% | 18.9% | ~ inconclusive | 6.0 MiB | 6.0 MiB | -+0.0% |
| layout.sparse_fragmented_2k | 102.843 | 102.505 | -1.1% | 3.9% | ~ inconclusive | 6.6 MiB | 6.6 MiB | +0.0% |
| query.builder_filter_sum_large | 163.198 | 145.234 | -9.2% | 20.5% | ~ inconclusive | 285.4 KiB | 285.4 KiB | +0.0% |
| query.groupby_category | 116.326 | 121.708 | +2.3% | 5.6% | ~ inconclusive | 418.1 KiB | 418.1 KiB | +0.0% |
| query.expr_groupby_orderby_limit | 133.536 | 137.854 | +3.2% | 7.1% | ~ inconclusive | 955.4 KiB | 955.4 KiB | +0.0% |
| query.rows_orderby_limit | 115.633 | 120.472 | +3.7% | 10.1% | ~ inconclusive | 528.4 KiB | 528.5 KiB | +0.0% |
| query.rows_limit_early_exit | 1.455 | 1.441 | -1.1% | 5.2% | ~ inconclusive | 439.3 KiB | 439.4 KiB | +0.0% |
| query.distinct_product | 121.880 | 125.704 | -3.0% | 7.0% | ~ inconclusive | 935.1 KiB | 935.1 KiB | +0.0% |
