base: ab-base-1 rev 73a14006be64 dirty clean-base-worktree XLSight.dll f33470fdcd6b (complete, 19824 ms); ab-base-2 rev 73a14006be64 dirty clean-base-worktree XLSight.dll f33470fdcd6b (complete, 20286 ms); ab-base-3 rev 73a14006be64 dirty clean-base-worktree XLSight.dll f33470fdcd6b (complete, 21066 ms)
cand: ab-cand-1 rev 73a14006be64 dirty src-diff-23f6eadf5f17 XLSight.dll e1ca1832435a (complete, 18810 ms); ab-cand-2 rev 73a14006be64 dirty src-diff-23f6eadf5f17 XLSight.dll e1ca1832435a (complete, 18948 ms); ab-cand-3 rev 73a14006be64 dirty src-diff-23f6eadf5f17 XLSight.dll e1ca1832435a (complete, 18783 ms)
mode: paired interleaved rounds (median of per-round deltas; verdict needs every round to agree in sign)

| case | base ms | cand ms | Δ time | noise ± | verdict | base alloc/op | cand alloc/op | Δ alloc |
|---|--:|--:|--:|--:|---|--:|--:|--:|
| mem.cold_scan_strings_unique | 355.543 | 352.921 | -0.7% | 3.0% | single-op (see RSS) | 20.1 MiB | 20.2 MiB | +0.1% |
| mem.cold_scan_strings_unique peak RSS | 75.3 MiB | 75.0 MiB | -0.4% | | | | | |
| mem.cold_analyze_banking_real | 376.753 | 312.366 | -17.1% | 3.0% | single-op (see RSS) | 4.4 MiB | 4.4 MiB | -+0.0% |
| mem.cold_analyze_banking_real peak RSS | 64.6 MiB | 64.6 MiB | +0.0% | | | | | |
| open.small | 0.159 | 0.154 | -9.4% | 22.3% | ~ inconclusive | 53.9 KiB | 53.9 KiB | +0.0% |
| open.banking_real | 7.308 | 6.828 | -5.5% | 10.6% | ~ inconclusive | 594.2 KiB | 594.2 KiB | -+0.0% |
| readcell.large_early_exit | 0.604 | 0.629 | +4.2% | 47.1% | ~ inconclusive | 87.1 KiB | 87.1 KiB | +0.0% |
| scan.numeric_xlsx | 144.641 | 114.412 | -21.3% | 7.3% | faster | 88.1 KiB | 88.1 KiB | +0.0% |
| scan.numeric_xlsb | 70.911 | 47.595 | -32.9% | 13.5% | ~ inconclusive | 28.6 KiB | 28.6 KiB | +0.0% |
| scan.strings_xlsb | 1.205 | 1.194 | +6.3% | 4.2% | ~ inconclusive | 54.7 KiB | 54.7 KiB | -+0.0% |
| scan.strings_repeated | 120.651 | 99.700 | -15.8% | 23.3% | ~ inconclusive | 306.1 KiB | 306.1 KiB | +0.0% |
| scan.strings_unique | 123.946 | 104.118 | -16.1% | 19.8% | ~ inconclusive | 19.7 MiB | 19.7 MiB | +0.0% |
| scan.mixed_sparse_values | 122.109 | 108.915 | -9.2% | 4.9% | ~ inconclusive | 2.6 MiB | 2.6 MiB | +0.0% |
| scan.mixed_sparse_formulas | 162.415 | 146.651 | -11.1% | 7.6% | ~ inconclusive | 5.1 MiB | 5.1 MiB | +0.0% |
| stream.snapshots_mixed | 136.997 | 119.655 | -8.6% | 7.0% | faster | 16.5 MiB | 16.5 MiB | +0.0% |
| scan.real_mcdonalds_all | 108.865 | 77.149 | -27.4% | 8.7% | faster | 764.1 KiB | 764.1 KiB | +0.0% |
| range.large_middle_rows | 35.850 | 36.265 | +1.4% | 6.5% | ~ inconclusive | 87.6 KiB | 87.6 KiB | +0.1% |
| range.readrange_medium | 4.882 | 4.168 | -15.2% | 21.1% | ~ inconclusive | 457.0 KiB | 457.0 KiB | +0.0% |
| range.async_large_middle_rows | 38.142 | 38.049 | -0.2% | 56.5% | ~ inconclusive | 197.8 KiB | 201.4 KiB | +1.8% |
| analyze.full_banking_real | 122.532 | 81.460 | -33.5% | 45.3% | ~ inconclusive | 3.8 MiB | 3.8 MiB | -+0.0% |
| analyze.observed_strings_repeated | 246.832 | 161.245 | -34.7% | 26.2% | ~ inconclusive | 469.6 KiB | 469.5 KiB | -+0.0% |
| analyze.full_merged_regions | 6.054 | 5.241 | -20.9% | 43.0% | ~ inconclusive | 477.9 KiB | 477.9 KiB | -+0.0% |
| layout.complex_calculator | 1.215 | 1.480 | +4.1% | 20.1% | ~ inconclusive | 288.5 KiB | 288.3 KiB | -0.1% |
| layout.mcdonalds_all | 196.205 | 174.321 | -4.8% | 40.1% | ~ inconclusive | 5.5 MiB | 5.5 MiB | +0.0% |
| layout.sparse_fragmented_2k | 35.721 | 37.584 | -2.6% | 21.8% | ~ inconclusive | 5.2 MiB | 5.2 MiB | +0.2% |
| query.builder_filter_sum_large | 132.811 | 133.292 | +0.4% | 15.1% | ~ inconclusive | 93.2 KiB | 93.2 KiB | +0.0% |
| query.groupby_category | 120.465 | 96.672 | -18.4% | 9.7% | faster | 217.9 KiB | 217.9 KiB | +0.0% |
| query.expr_groupby_orderby_limit | 125.218 | 108.360 | -16.5% | 9.9% | faster | 755.4 KiB | 764.2 KiB | +1.2% |
| query.rows_orderby_limit | 106.184 | 95.954 | -10.5% | 13.7% | ~ inconclusive | 328.3 KiB | 328.5 KiB | +0.1% |
| query.rows_limit_early_exit | 1.395 | 1.292 | -8.2% | 5.5% | faster | 239.3 KiB | 239.3 KiB | -+0.0% |
| query.distinct_product | 109.136 | 91.502 | -18.1% | 7.9% | ~ inconclusive | 735.0 KiB | 734.9 KiB | -+0.0% |
