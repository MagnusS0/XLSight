base: ab-base-1 rev 27fc006a4066 dirty clean-base-worktree XLSight.dll b34f4bae5b02 (complete, 21253 ms); ab-base-2 rev 27fc006a4066 dirty clean-base-worktree XLSight.dll b34f4bae5b02 (complete, 19185 ms); ab-base-3 rev 27fc006a4066 dirty clean-base-worktree XLSight.dll b34f4bae5b02 (complete, 19370 ms)
cand: ab-cand-1 rev 27fc006a4066 dirty src-diff-3f082e330d44 XLSight.dll 58ed9be1f5be (complete, 19409 ms); ab-cand-2 rev 27fc006a4066 dirty src-diff-3f082e330d44 XLSight.dll 58ed9be1f5be (complete, 19271 ms); ab-cand-3 rev 27fc006a4066 dirty src-diff-3f082e330d44 XLSight.dll 58ed9be1f5be (complete, 19069 ms)
mode: paired interleaved rounds (median of per-round deltas; verdict needs every round to agree in sign)

| case | base ms | cand ms | Δ time | noise ± | verdict | base alloc/op | cand alloc/op | Δ alloc |
|---|--:|--:|--:|--:|---|--:|--:|--:|
| mem.cold_scan_strings_unique | 336.384 | 321.819 | -4.3% | 3.0% | single-op (see RSS) | 20.1 MiB | 20.1 MiB | +0.0% |
| mem.cold_scan_strings_unique peak RSS | 75.5 MiB | 75.2 MiB | -0.4% | | | | | |
| mem.cold_analyze_banking_real | 303.920 | 356.499 | +17.3% | 3.0% | single-op (see RSS) | 4.4 MiB | 4.4 MiB | -0.7% |
| mem.cold_analyze_banking_real peak RSS | 63.9 MiB | 64.6 MiB | +1.1% | | | | | |
| open.small | 0.153 | 0.155 | -1.2% | 34.1% | ~ inconclusive | 53.9 KiB | 53.9 KiB | +0.0% |
| open.banking_real | 7.841 | 6.687 | -17.8% | 5.6% | ~ inconclusive | 594.2 KiB | 594.2 KiB | -+0.0% |
| readcell.large_early_exit | 0.564 | 0.561 | +3.2% | 65.4% | ~ inconclusive | 87.1 KiB | 87.1 KiB | +0.0% |
| scan.numeric_xlsx | 140.273 | 155.907 | +13.1% | 16.2% | ~ inconclusive | 88.1 KiB | 88.1 KiB | +0.0% |
| scan.numeric_xlsb | 51.748 | 48.350 | +0.8% | 12.6% | ~ inconclusive | 28.5 KiB | 28.6 KiB | +0.2% |
| scan.strings_xlsb | 1.205 | 1.221 | +3.8% | 11.5% | ~ inconclusive | 54.7 KiB | 54.7 KiB | +0.0% |
| scan.strings_repeated | 111.883 | 110.304 | -0.7% | 8.2% | ~ inconclusive | 306.2 KiB | 306.1 KiB | -+0.0% |
| scan.strings_unique | 110.275 | 116.339 | +5.5% | 15.4% | ~ inconclusive | 19.7 MiB | 19.7 MiB | +0.0% |
| scan.mixed_sparse_values | 127.907 | 121.398 | -2.9% | 12.1% | ~ inconclusive | 2.6 MiB | 2.6 MiB | +0.0% |
| scan.mixed_sparse_formulas | 151.110 | 151.556 | +0.3% | 3.9% | ~ inconclusive | 5.1 MiB | 5.1 MiB | +0.0% |
| stream.snapshots_mixed | 128.205 | 127.268 | -0.2% | 8.7% | ~ inconclusive | 16.5 MiB | 16.5 MiB | +0.0% |
| scan.real_mcdonalds_all | 92.901 | 92.335 | -0.2% | 7.7% | ~ inconclusive | 764.1 KiB | 764.1 KiB | +0.0% |
| range.large_middle_rows | 35.797 | 35.421 | +0.5% | 4.2% | ~ inconclusive | 87.6 KiB | 87.6 KiB | +0.0% |
| range.readrange_medium | 4.440 | 4.497 | +1.9% | 17.4% | ~ inconclusive | 457.0 KiB | 457.0 KiB | +0.0% |
| range.async_large_middle_rows | 38.298 | 39.671 | +2.6% | 9.2% | ~ inconclusive | 201.2 KiB | 196.3 KiB | -2.4% |
| analyze.full_banking_real | 106.059 | 94.976 | -11.7% | 54.3% | ~ inconclusive | 3.7 MiB | 3.8 MiB | +0.6% |
| analyze.observed_strings_repeated | 182.645 | 200.189 | +3.5% | 18.5% | ~ inconclusive | 596.9 KiB | 469.5 KiB | -21.3% |
| analyze.full_merged_regions | 7.082 | 5.585 | -11.0% | 7.3% | faster | 480.0 KiB | 477.9 KiB | -0.4% |
| layout.complex_calculator | 1.299 | 1.220 | +3.4% | 117.0% | ~ inconclusive | 288.4 KiB | 288.6 KiB | +0.1% |
| layout.mcdonalds_all | 178.492 | 174.268 | +0.5% | 40.8% | ~ inconclusive | 5.5 MiB | 5.6 MiB | +0.2% |
| layout.sparse_fragmented_2k | 32.329 | 34.055 | -0.5% | 10.9% | ~ inconclusive | 5.2 MiB | 5.2 MiB | -+0.0% |
| query.builder_filter_sum_large | 136.611 | 136.985 | -5.0% | 9.1% | ~ inconclusive | 93.2 KiB | 93.2 KiB | +0.0% |
| query.groupby_category | 104.775 | 116.497 | +11.2% | 9.6% | SLOWER | 217.9 KiB | 218.1 KiB | +0.1% |
| query.expr_groupby_orderby_limit | 122.272 | 123.245 | +0.8% | 26.0% | ~ inconclusive | 755.4 KiB | 755.4 KiB | +0.0% |
| query.rows_orderby_limit | 105.365 | 103.912 | -1.1% | 20.4% | ~ inconclusive | 328.5 KiB | 328.3 KiB | -0.1% |
| query.rows_limit_early_exit | 1.371 | 1.377 | +3.3% | 5.9% | ~ inconclusive | 239.3 KiB | 239.3 KiB | +0.0% |
| query.distinct_product | 105.122 | 104.018 | -1.1% | 38.5% | ~ inconclusive | 735.1 KiB | 734.9 KiB | -+0.0% |
