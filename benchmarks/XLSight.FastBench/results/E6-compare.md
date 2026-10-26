base: ab-base-1 rev 3db33154bdba dirty clean-base-worktree XLSight.dll f20dd80186a5 (complete, 20321 ms); ab-base-2 rev 3db33154bdba dirty clean-base-worktree XLSight.dll f20dd80186a5 (complete, 20317 ms); ab-base-3 rev 3db33154bdba dirty clean-base-worktree XLSight.dll f20dd80186a5 (complete, 19742 ms)
cand: ab-cand-1 rev 3db33154bdba dirty src-diff-c4d0be38b7a3 XLSight.dll 115fc99078c0 (complete, 19741 ms); ab-cand-2 rev 3db33154bdba dirty src-diff-c4d0be38b7a3 XLSight.dll 115fc99078c0 (complete, 19974 ms); ab-cand-3 rev 3db33154bdba dirty src-diff-c4d0be38b7a3 XLSight.dll 115fc99078c0 (complete, 19374 ms)
mode: paired interleaved rounds (median of per-round deltas; verdict needs every round to agree in sign)

| case | base ms | cand ms | Δ time | noise ± | verdict | base alloc/op | cand alloc/op | Δ alloc |
|---|--:|--:|--:|--:|---|--:|--:|--:|
| mem.cold_scan_strings_unique | 317.664 | 296.655 | -6.6% | 3.0% | single-op (see RSS) | 20.1 MiB | 20.1 MiB | +0.0% |
| mem.cold_scan_strings_unique peak RSS | 75.3 MiB | 75.2 MiB | -0.2% | | | | | |
| mem.cold_analyze_banking_real | 269.122 | 264.415 | -1.7% | 3.0% | single-op (see RSS) | 4.4 MiB | 4.4 MiB | +0.4% |
| mem.cold_analyze_banking_real peak RSS | 63.4 MiB | 63.9 MiB | +0.8% | | | | | |
| open.small | 0.151 | 0.155 | +2.8% | 17.8% | ~ inconclusive | 53.9 KiB | 53.9 KiB | +0.0% |
| open.banking_real | 6.890 | 6.606 | -3.2% | 6.3% | ~ inconclusive | 594.2 KiB | 594.2 KiB | +0.0% |
| readcell.large_early_exit | 0.574 | 0.549 | -2.2% | 5.3% | ~ inconclusive | 87.1 KiB | 87.1 KiB | +0.0% |
| scan.numeric_xlsx | 149.448 | 130.923 | -13.1% | 8.8% | ~ inconclusive | 88.1 KiB | 88.1 KiB | +0.0% |
| scan.numeric_xlsb | 48.805 | 46.564 | -4.1% | 18.4% | ~ inconclusive | 28.6 KiB | 28.6 KiB | +0.0% |
| scan.strings_xlsb | 1.132 | 1.102 | -1.9% | 10.5% | ~ inconclusive | 54.7 KiB | 54.7 KiB | +0.0% |
| scan.strings_repeated | 110.077 | 110.182 | +2.1% | 21.8% | ~ inconclusive | 306.1 KiB | 306.2 KiB | +0.0% |
| scan.strings_unique | 109.400 | 108.937 | +3.3% | 8.9% | ~ inconclusive | 19.7 MiB | 19.7 MiB | +0.0% |
| scan.mixed_sparse_values | 122.124 | 121.964 | +1.8% | 3.0% | ~ inconclusive | 2.6 MiB | 2.6 MiB | +0.0% |
| scan.mixed_sparse_formulas | 151.472 | 153.569 | +1.4% | 5.1% | ~ inconclusive | 5.1 MiB | 5.1 MiB | +0.0% |
| stream.snapshots_mixed | 129.329 | 130.302 | +5.9% | 15.6% | ~ inconclusive | 16.5 MiB | 16.5 MiB | -+0.0% |
| scan.real_mcdonalds_all | 92.896 | 92.294 | -0.1% | 19.4% | ~ inconclusive | 764.1 KiB | 764.2 KiB | +0.0% |
| range.large_middle_rows | 53.172 | 34.461 | -35.7% | 3.1% | faster | 87.6 KiB | 87.7 KiB | +0.1% |
| range.readrange_medium | 4.522 | 4.685 | +1.1% | 14.6% | ~ inconclusive | 457.0 KiB | 457.0 KiB | +0.0% |
| range.async_large_middle_rows | 58.867 | 41.627 | -29.3% | 24.4% | faster | 201.6 KiB | 200.7 KiB | -0.4% |
| analyze.full_banking_real | 76.660 | 88.725 | +15.7% | 23.6% | ~ inconclusive | 3.8 MiB | 3.8 MiB | +0.5% |
| analyze.observed_strings_repeated | 211.361 | 200.384 | -6.1% | 23.1% | ~ inconclusive | 596.9 KiB | 596.9 KiB | +0.0% |
| analyze.full_merged_regions | 210.088 | 181.681 | -13.5% | 11.9% | ~ inconclusive | 480.7 KiB | 480.7 KiB | +0.0% |
| layout.complex_calculator | 1.368 | 1.360 | -0.6% | 7.7% | ~ inconclusive | 291.7 KiB | 291.7 KiB | +0.0% |
| layout.mcdonalds_all | 179.280 | 168.829 | +2.9% | 14.5% | ~ inconclusive | 6.0 MiB | 6.0 MiB | -0.2% |
| layout.sparse_fragmented_2k | 113.487 | 104.703 | -8.8% | 9.4% | ~ inconclusive | 6.2 MiB | 6.2 MiB | +0.0% |
| query.builder_filter_sum_large | 136.212 | 129.224 | -4.8% | 18.0% | ~ inconclusive | 93.3 KiB | 93.3 KiB | +0.0% |
| query.groupby_category | 116.334 | 112.436 | -5.9% | 20.7% | ~ inconclusive | 218.0 KiB | 218.0 KiB | +0.0% |
| query.expr_groupby_orderby_limit | 140.214 | 123.676 | -12.0% | 16.2% | ~ inconclusive | 755.3 KiB | 755.4 KiB | +0.0% |
| query.rows_orderby_limit | 120.067 | 104.557 | -5.1% | 4.1% | faster | 328.4 KiB | 328.2 KiB | -0.1% |
| query.rows_limit_early_exit | 1.425 | 1.359 | -4.8% | 10.1% | ~ inconclusive | 239.3 KiB | 239.3 KiB | +0.0% |
| query.distinct_product | 129.978 | 120.199 | -2.7% | 11.3% | ~ inconclusive | 735.1 KiB | 735.1 KiB | +0.0% |
