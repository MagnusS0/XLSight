base: ab-base-1 rev 232b51521ebe dirty clean-base-worktree XLSight.dll 6585d82b1e76 (complete, 19198 ms); ab-base-2 rev 232b51521ebe dirty clean-base-worktree XLSight.dll 6585d82b1e76 (complete, 19466 ms); ab-base-3 rev 232b51521ebe dirty clean-base-worktree XLSight.dll 6585d82b1e76 (complete, 19042 ms)
cand: ab-cand-1 rev 232b51521ebe dirty src-diff-834deb3c7ff5 XLSight.dll 6585d82b1e76 (complete, 18877 ms); ab-cand-2 rev 232b51521ebe dirty src-diff-834deb3c7ff5 XLSight.dll 6585d82b1e76 (complete, 19647 ms); ab-cand-3 rev 232b51521ebe dirty src-diff-834deb3c7ff5 XLSight.dll 6585d82b1e76 (complete, 19474 ms)
mode: paired interleaved rounds (median of per-round deltas; verdict needs every round to agree in sign)

| case | base ms | cand ms | Δ time | noise ± | verdict | base alloc/op | cand alloc/op | Δ alloc |
|---|--:|--:|--:|--:|---|--:|--:|--:|
| mem.cold_scan_strings_unique | 301.502 | 305.206 | +1.2% | 3.0% | single-op (see RSS) | 20.1 MiB | 20.1 MiB | +0.0% |
| mem.cold_scan_strings_unique peak RSS | 75.4 MiB | 75.2 MiB | -0.2% | | | | | |
| mem.cold_analyze_banking_real | 273.867 | 339.043 | +23.8% | 3.0% | single-op (see RSS) | 4.4 MiB | 4.4 MiB | -+0.0% |
| mem.cold_analyze_banking_real peak RSS | 63.9 MiB | 64.0 MiB | +0.2% | | | | | |
| open.small | 0.197 | 0.157 | -18.8% | 20.6% | ~ inconclusive | 53.9 KiB | 53.9 KiB | +0.0% |
| open.banking_real | 6.968 | 6.542 | -6.1% | 16.4% | ~ inconclusive | 594.2 KiB | 594.2 KiB | -+0.0% |
| readcell.large_early_exit | 0.605 | 0.548 | -8.7% | 11.5% | ~ inconclusive | 87.1 KiB | 87.1 KiB | -+0.0% |
| scan.numeric_xlsx | 147.259 | 141.051 | -4.2% | 23.2% | ~ inconclusive | 88.1 KiB | 88.1 KiB | +0.0% |
| scan.numeric_xlsb | 46.937 | 48.276 | +2.9% | 10.1% | ~ inconclusive | 28.6 KiB | 28.6 KiB | +0.0% |
| scan.strings_xlsb | 1.198 | 1.198 | +0.0% | 22.3% | ~ inconclusive | 54.7 KiB | 54.7 KiB | +0.0% |
| scan.strings_repeated | 109.258 | 116.917 | +8.6% | 8.4% | ~ inconclusive | 306.2 KiB | 306.1 KiB | -+0.0% |
| scan.strings_unique | 112.127 | 114.998 | +2.6% | 6.1% | ~ inconclusive | 19.7 MiB | 19.7 MiB | +0.0% |
| scan.mixed_sparse_values | 123.865 | 122.741 | -3.8% | 3.6% | faster | 2.6 MiB | 2.6 MiB | +0.0% |
| scan.mixed_sparse_formulas | 151.318 | 147.611 | +0.1% | 18.1% | ~ inconclusive | 5.1 MiB | 5.1 MiB | +0.0% |
| stream.snapshots_mixed | 128.131 | 122.474 | -4.4% | 13.0% | ~ inconclusive | 16.5 MiB | 16.5 MiB | +0.0% |
| scan.real_mcdonalds_all | 102.795 | 93.643 | -11.1% | 6.5% | ~ inconclusive | 764.2 KiB | 764.1 KiB | -+0.0% |
| range.large_middle_rows | 34.913 | 34.438 | -0.2% | 4.1% | ~ inconclusive | 87.7 KiB | 87.6 KiB | -0.1% |
| range.readrange_medium | 4.599 | 5.135 | +11.0% | 34.8% | ~ inconclusive | 457.0 KiB | 457.0 KiB | -+0.0% |
| range.async_large_middle_rows | 42.221 | 42.854 | +2.0% | 12.9% | ~ inconclusive | 196.8 KiB | 201.5 KiB | +2.4% |
| analyze.full_banking_real | 73.883 | 88.613 | +19.9% | 37.1% | ~ inconclusive | 3.8 MiB | 3.8 MiB | +0.0% |
| analyze.observed_strings_repeated | 188.746 | 212.351 | +12.5% | 40.1% | ~ inconclusive | 596.8 KiB | 596.8 KiB | +0.0% |
| analyze.full_merged_regions | 7.617 | 6.601 | -14.5% | 11.3% | faster | 480.0 KiB | 480.0 KiB | +0.0% |
| layout.complex_calculator | 1.369 | 1.172 | -14.4% | 6.2% | faster | 291.5 KiB | 286.6 KiB | -1.7% |
| layout.mcdonalds_all | 175.461 | 165.495 | -5.7% | 16.5% | ~ inconclusive | 6.0 MiB | 5.5 MiB | -7.7% |
| layout.sparse_fragmented_2k | 99.421 | 97.479 | +0.4% | 13.4% | ~ inconclusive | 6.2 MiB | 5.3 MiB | -14.3% |
| query.builder_filter_sum_large | 132.733 | 137.304 | +1.8% | 19.7% | ~ inconclusive | 93.3 KiB | 93.2 KiB | -0.2% |
| query.groupby_category | 106.841 | 131.837 | +22.7% | 26.0% | ~ inconclusive | 217.9 KiB | 218.0 KiB | +0.1% |
| query.expr_groupby_orderby_limit | 123.988 | 125.724 | +2.3% | 10.1% | ~ inconclusive | 755.4 KiB | 755.3 KiB | -+0.0% |
| query.rows_orderby_limit | 101.619 | 116.163 | +15.2% | 6.3% | ~ inconclusive | 328.2 KiB | 328.4 KiB | +0.1% |
| query.rows_limit_early_exit | 1.425 | 1.380 | -3.8% | 8.3% | ~ inconclusive | 239.3 KiB | 239.3 KiB | -+0.0% |
| query.distinct_product | 106.160 | 110.110 | +3.7% | 19.8% | ~ inconclusive | 734.9 KiB | 735.1 KiB | +0.0% |
