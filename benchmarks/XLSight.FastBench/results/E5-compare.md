base: ab-base-1 rev bc724d9ad5a5 dirty clean-base-worktree XLSight.dll ff23ab500646 (complete, 19799 ms); ab-base-2 rev bc724d9ad5a5 dirty clean-base-worktree XLSight.dll ff23ab500646 (complete, 20422 ms); ab-base-3 rev bc724d9ad5a5 dirty clean-base-worktree XLSight.dll ff23ab500646 (complete, 20577 ms)
cand: ab-cand-1 rev bc724d9ad5a5 dirty src-diff-547971d44fb9 XLSight.dll bb765b7aaa79 (complete, 19877 ms); ab-cand-2 rev bc724d9ad5a5 dirty src-diff-547971d44fb9 XLSight.dll bb765b7aaa79 (complete, 20103 ms); ab-cand-3 rev bc724d9ad5a5 dirty src-diff-547971d44fb9 XLSight.dll bb765b7aaa79 (complete, 20526 ms)
mode: paired interleaved rounds (median of per-round deltas; verdict needs every round to agree in sign)

| case | base ms | cand ms | Δ time | noise ± | verdict | base alloc/op | cand alloc/op | Δ alloc |
|---|--:|--:|--:|--:|---|--:|--:|--:|
| mem.cold_scan_strings_unique | 326.101 | 347.900 | +6.7% | 3.0% | single-op (see RSS) | 20.1 MiB | 20.1 MiB | +0.0% |
| mem.cold_scan_strings_unique peak RSS | 75.1 MiB | 75.1 MiB | +0.0% | | | | | |
| mem.cold_analyze_banking_real | 305.298 | 262.293 | -14.1% | 3.0% | single-op (see RSS) | 4.4 MiB | 4.4 MiB | -0.1% |
| mem.cold_analyze_banking_real peak RSS | 63.2 MiB | 63.2 MiB | -+0.0% | | | | | |
| open.small | 0.150 | 0.158 | +4.1% | 21.2% | ~ inconclusive | 53.9 KiB | 53.9 KiB | +0.0% |
| open.banking_real | 6.465 | 6.606 | +0.0% | 6.1% | ~ inconclusive | 594.2 KiB | 594.2 KiB | +0.0% |
| readcell.large_early_exit | 0.546 | 0.558 | +2.7% | 8.0% | ~ inconclusive | 87.1 KiB | 87.1 KiB | +0.0% |
| scan.numeric_xlsx | 131.935 | 138.095 | +3.0% | 15.6% | ~ inconclusive | 88.1 KiB | 88.1 KiB | +0.0% |
| scan.numeric_xlsb | 46.584 | 62.328 | +33.8% | 22.3% | ~ inconclusive | 28.6 KiB | 28.6 KiB | +0.0% |
| scan.strings_xlsb | 1.172 | 1.150 | +1.0% | 36.7% | ~ inconclusive | 54.7 KiB | 54.7 KiB | +0.0% |
| scan.strings_repeated | 115.522 | 109.698 | +0.2% | 12.9% | ~ inconclusive | 306.1 KiB | 306.2 KiB | +0.0% |
| scan.strings_unique | 121.252 | 113.008 | -5.4% | 12.7% | ~ inconclusive | 19.7 MiB | 19.7 MiB | +0.0% |
| scan.mixed_sparse_values | 136.270 | 128.918 | -5.5% | 11.5% | ~ inconclusive | 4.4 MiB | 2.6 MiB | -41.3% |
| scan.mixed_sparse_formulas | 149.971 | 150.088 | +0.1% | 33.2% | ~ inconclusive | 9.1 MiB | 5.1 MiB | -43.9% |
| stream.snapshots_mixed | 129.228 | 128.447 | -0.6% | 17.2% | ~ inconclusive | 18.3 MiB | 16.5 MiB | -9.9% |
| scan.real_mcdonalds_all | 92.588 | 96.501 | +7.7% | 7.9% | ~ inconclusive | 772.1 KiB | 764.2 KiB | -1.0% |
| range.large_middle_rows | 50.405 | 50.333 | -1.0% | 7.1% | ~ inconclusive | 87.6 KiB | 87.7 KiB | +0.1% |
| range.readrange_medium | 4.603 | 4.635 | -0.7% | 15.7% | ~ inconclusive | 457.0 KiB | 457.1 KiB | +0.0% |
| range.async_large_middle_rows | 54.411 | 54.375 | -4.3% | 17.3% | ~ inconclusive | 199.0 KiB | 198.7 KiB | -0.1% |
| analyze.full_banking_real | 82.450 | 86.522 | +4.9% | 16.7% | ~ inconclusive | 3.8 MiB | 3.8 MiB | -+0.0% |
| analyze.observed_strings_repeated | 221.470 | 220.763 | -9.2% | 33.6% | ~ inconclusive | 596.9 KiB | 596.9 KiB | +0.0% |
| analyze.full_merged_regions | 182.112 | 212.199 | +0.8% | 21.0% | ~ inconclusive | 480.7 KiB | 480.7 KiB | +0.0% |
| layout.complex_calculator | 1.375 | 1.368 | -1.9% | 8.5% | ~ inconclusive | 291.7 KiB | 291.7 KiB | +0.0% |
| layout.mcdonalds_all | 197.318 | 180.245 | -8.7% | 43.4% | ~ inconclusive | 6.0 MiB | 6.0 MiB | -0.1% |
| layout.sparse_fragmented_2k | 122.425 | 102.863 | -18.5% | 8.8% | faster | 6.3 MiB | 6.2 MiB | -1.9% |
| query.builder_filter_sum_large | 143.850 | 143.796 | -+0.0% | 5.4% | ~ inconclusive | 93.3 KiB | 93.3 KiB | +0.0% |
| query.groupby_category | 121.548 | 122.735 | +4.2% | 10.4% | ~ inconclusive | 218.0 KiB | 218.0 KiB | +0.0% |
| query.expr_groupby_orderby_limit | 130.151 | 127.039 | -0.5% | 11.5% | ~ inconclusive | 755.3 KiB | 755.4 KiB | +0.0% |
| query.rows_orderby_limit | 110.865 | 109.450 | -2.4% | 4.6% | ~ inconclusive | 328.4 KiB | 328.4 KiB | +0.0% |
| query.rows_limit_early_exit | 1.372 | 1.346 | -4.0% | 6.2% | ~ inconclusive | 239.3 KiB | 239.3 KiB | +0.0% |
| query.distinct_product | 128.046 | 118.092 | -6.0% | 9.1% | ~ inconclusive | 735.1 KiB | 735.1 KiB | +0.0% |
