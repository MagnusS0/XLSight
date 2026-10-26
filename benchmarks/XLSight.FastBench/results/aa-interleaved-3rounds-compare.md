base: ab-base-1 rev ced7260be147 dirty clean-base-worktree XLSight.dll a715b693529f (complete, 21056 ms); ab-base-2 rev ced7260be147 dirty clean-base-worktree XLSight.dll a715b693529f (complete, 20491 ms); ab-base-3 rev ced7260be147 dirty clean-base-worktree XLSight.dll a715b693529f (complete, 20114 ms)
cand: ab-cand-1 rev ced7260be147 dirty clean XLSight.dll a715b693529f (complete, 20564 ms); ab-cand-2 rev ced7260be147 dirty clean XLSight.dll a715b693529f (complete, 20689 ms); ab-cand-3 rev ced7260be147 dirty clean XLSight.dll a715b693529f (complete, 20348 ms)
mode: paired interleaved rounds (median of per-round deltas; verdict needs every round to agree in sign)

| case | base ms | cand ms | Δ time | noise ± | verdict | base alloc/op | cand alloc/op | Δ alloc |
|---|--:|--:|--:|--:|---|--:|--:|--:|
| mem.cold_scan_strings_unique | 267.378 | 307.195 | +14.9% | 3.0% | single-op (see RSS) | 20.3 MiB | 20.3 MiB | +0.0% |
| mem.cold_scan_strings_unique peak RSS | 75.6 MiB | 75.5 MiB | -0.1% | | | | | |
| mem.cold_analyze_banking_real | 332.554 | 304.745 | -8.4% | 3.0% | single-op (see RSS) | 5.3 MiB | 5.3 MiB | +0.4% |
| mem.cold_analyze_banking_real peak RSS | 64.2 MiB | 64.1 MiB | -+0.0% | | | | | |
| open.small | 0.184 | 0.191 | +6.5% | 79.3% | ~ inconclusive | 182.0 KiB | 182.0 KiB | +0.0% |
| open.banking_real | 6.856 | 6.843 | +2.9% | 11.7% | ~ inconclusive | 658.3 KiB | 658.3 KiB | +0.0% |
| readcell.large_early_exit | 0.615 | 0.659 | +5.2% | 7.9% | ~ inconclusive | 279.2 KiB | 279.2 KiB | +0.0% |
| scan.numeric_xlsx | 149.709 | 151.191 | +1.9% | 38.7% | ~ inconclusive | 280.2 KiB | 280.1 KiB | -+0.0% |
| scan.numeric_xlsb | 47.495 | 48.191 | -0.7% | 5.8% | ~ inconclusive | 28.6 KiB | 28.6 KiB | +0.0% |
| scan.strings_xlsb | 1.118 | 1.117 | +0.4% | 4.5% | ~ inconclusive | 54.7 KiB | 54.7 KiB | -+0.0% |
| scan.strings_repeated | 116.558 | 122.836 | +5.6% | 25.4% | ~ inconclusive | 506.2 KiB | 506.2 KiB | +0.0% |
| scan.strings_unique | 108.512 | 113.747 | +4.8% | 7.5% | ~ inconclusive | 19.9 MiB | 19.9 MiB | +0.0% |
| scan.mixed_sparse_values | 134.068 | 133.119 | -0.7% | 21.5% | ~ inconclusive | 4.6 MiB | 4.6 MiB | +0.0% |
| scan.mixed_sparse_formulas | 155.755 | 149.251 | -4.2% | 5.1% | ~ inconclusive | 9.3 MiB | 9.3 MiB | +0.0% |
| stream.snapshots_mixed | 133.111 | 132.363 | -3.0% | 6.2% | ~ inconclusive | 18.5 MiB | 18.5 MiB | +0.0% |
| scan.real_mcdonalds_all | 103.492 | 99.057 | -1.9% | 10.9% | ~ inconclusive | 844.1 KiB | 844.1 KiB | +0.0% |
| range.large_middle_rows | 48.818 | 50.928 | +4.3% | 6.9% | ~ inconclusive | 279.8 KiB | 279.8 KiB | +0.0% |
| range.readrange_medium | 4.735 | 4.805 | +2.1% | 20.6% | ~ inconclusive | 649.1 KiB | 649.1 KiB | -+0.0% |
| range.async_large_middle_rows | 141.970 | 132.386 | -6.8% | 13.7% | ~ inconclusive | 14.3 MiB | 15.1 MiB | +5.6% |
| analyze.full_banking_real | 83.057 | 93.096 | +13.5% | 19.8% | ~ inconclusive | 4.6 MiB | 4.6 MiB | +0.0% |
| analyze.observed_strings_repeated | 211.071 | 191.336 | -11.8% | 14.5% | ~ inconclusive | 860.9 KiB | 860.9 KiB | +0.0% |
| analyze.full_merged_regions | 180.650 | 205.058 | +13.5% | 17.3% | ~ inconclusive | 744.7 KiB | 744.8 KiB | +0.0% |
| layout.complex_calculator | 1.477 | 1.508 | +3.0% | 11.2% | ~ inconclusive | 481.5 KiB | 481.6 KiB | +0.0% |
| layout.mcdonalds_all | 181.386 | 196.953 | +1.7% | 15.5% | ~ inconclusive | 6.0 MiB | 6.0 MiB | +0.0% |
| layout.sparse_fragmented_2k | 102.834 | 103.182 | +3.5% | 13.5% | ~ inconclusive | 6.6 MiB | 6.6 MiB | +0.0% |
| query.builder_filter_sum_large | 149.817 | 145.006 | -3.2% | 6.5% | ~ inconclusive | 285.4 KiB | 285.4 KiB | +0.0% |
| query.groupby_category | 128.504 | 127.061 | -7.5% | 15.2% | ~ inconclusive | 418.1 KiB | 418.1 KiB | +0.0% |
| query.expr_groupby_orderby_limit | 135.433 | 137.967 | +3.9% | 22.4% | ~ inconclusive | 955.4 KiB | 955.4 KiB | +0.0% |
| query.rows_orderby_limit | 119.709 | 128.844 | +5.2% | 14.8% | ~ inconclusive | 528.4 KiB | 528.4 KiB | +0.0% |
| query.rows_limit_early_exit | 1.444 | 1.521 | +1.0% | 99.2% | ~ inconclusive | 439.3 KiB | 439.3 KiB | +0.0% |
| query.distinct_product | 125.688 | 116.917 | -7.2% | 4.7% | ~ inconclusive | 935.1 KiB | 935.1 KiB | +0.0% |
