base: ab-base-1 rev 16976e42826d dirty clean-base-worktree XLSight.dll c6da1ee76991 (complete, 20083 ms); ab-base-2 rev 16976e42826d dirty clean-base-worktree XLSight.dll c6da1ee76991 (complete, 20217 ms); ab-base-3 rev 16976e42826d dirty clean-base-worktree XLSight.dll c6da1ee76991 (complete, 20368 ms)
cand: ab-cand-1 rev 16976e42826d dirty src-diff-74105ec37350 XLSight.dll 0e24b4827103 (complete, 20519 ms); ab-cand-2 rev 16976e42826d dirty src-diff-74105ec37350 XLSight.dll 0e24b4827103 (complete, 20511 ms); ab-cand-3 rev 16976e42826d dirty src-diff-74105ec37350 XLSight.dll 0e24b4827103 (complete, 20515 ms)
mode: paired interleaved rounds (median of per-round deltas; verdict needs every round to agree in sign)

| case | base ms | cand ms | Δ time | noise ± | verdict | base alloc/op | cand alloc/op | Δ alloc |
|---|--:|--:|--:|--:|---|--:|--:|--:|
| mem.cold_scan_strings_unique | 318.449 | 313.827 | -1.5% | 3.0% | single-op (see RSS) | 20.1 MiB | 20.1 MiB | +0.0% |
| mem.cold_scan_strings_unique peak RSS | 75.3 MiB | 75.2 MiB | -0.1% | | | | | |
| mem.cold_analyze_banking_real | 334.342 | 293.771 | -12.1% | 3.0% | single-op (see RSS) | 4.4 MiB | 4.4 MiB | +0.4% |
| mem.cold_analyze_banking_real peak RSS | 63.3 MiB | 63.3 MiB | +0.1% | | | | | |
| open.small | 0.194 | 0.197 | -20.0% | 33.6% | ~ inconclusive | 53.9 KiB | 53.9 KiB | +0.0% |
| open.banking_real | 7.016 | 6.938 | -10.8% | 19.7% | ~ inconclusive | 594.2 KiB | 594.2 KiB | -+0.0% |
| readcell.large_early_exit | 0.552 | 0.571 | +2.6% | 11.4% | ~ inconclusive | 87.1 KiB | 87.1 KiB | +0.0% |
| scan.numeric_xlsx | 148.536 | 154.395 | -0.2% | 16.0% | ~ inconclusive | 88.1 KiB | 88.1 KiB | +0.0% |
| scan.numeric_xlsb | 46.922 | 47.979 | +2.3% | 10.3% | ~ inconclusive | 28.6 KiB | 28.6 KiB | +0.0% |
| scan.strings_xlsb | 1.191 | 1.208 | +2.9% | 21.3% | ~ inconclusive | 54.7 KiB | 54.7 KiB | +0.0% |
| scan.strings_repeated | 119.654 | 111.747 | -11.3% | 4.9% | faster | 306.2 KiB | 306.2 KiB | -+0.0% |
| scan.strings_unique | 113.398 | 116.933 | +9.2% | 9.0% | ~ inconclusive | 19.7 MiB | 19.7 MiB | +0.0% |
| scan.mixed_sparse_values | 134.657 | 136.884 | +1.7% | 15.1% | ~ inconclusive | 4.4 MiB | 4.4 MiB | +0.0% |
| scan.mixed_sparse_formulas | 155.126 | 152.611 | -0.3% | 4.4% | ~ inconclusive | 9.1 MiB | 9.1 MiB | +0.0% |
| stream.snapshots_mixed | 141.987 | 137.198 | -5.3% | 15.0% | ~ inconclusive | 18.3 MiB | 18.3 MiB | +0.0% |
| scan.real_mcdonalds_all | 99.760 | 96.566 | -3.2% | 12.8% | ~ inconclusive | 772.2 KiB | 772.2 KiB | +0.0% |
| range.large_middle_rows | 51.647 | 52.374 | +4.7% | 6.2% | ~ inconclusive | 87.7 KiB | 87.7 KiB | +0.0% |
| range.readrange_medium | 5.228 | 4.469 | -12.9% | 16.6% | ~ inconclusive | 457.0 KiB | 457.0 KiB | -+0.0% |
| range.async_large_middle_rows | 56.926 | 58.537 | +8.2% | 9.8% | ~ inconclusive | 207.4 KiB | 199.5 KiB | -3.8% |
| analyze.full_banking_real | 89.539 | 88.479 | -1.7% | 15.9% | ~ inconclusive | 3.7 MiB | 3.8 MiB | +1.7% |
| analyze.observed_strings_repeated | 190.567 | 184.360 | -3.3% | 30.6% | ~ inconclusive | 596.9 KiB | 596.8 KiB | -+0.0% |
| analyze.full_merged_regions | 199.745 | 203.387 | +1.8% | 35.4% | ~ inconclusive | 480.7 KiB | 480.7 KiB | -+0.0% |
| layout.complex_calculator | 1.336 | 1.332 | -0.7% | 4.3% | ~ inconclusive | 291.7 KiB | 291.5 KiB | -+0.0% |
| layout.mcdonalds_all | 189.758 | 201.653 | +6.3% | 50.9% | ~ inconclusive | 6.0 MiB | 6.0 MiB | +0.0% |
| layout.sparse_fragmented_2k | 114.187 | 108.634 | +1.5% | 29.1% | ~ inconclusive | 6.4 MiB | 6.4 MiB | -+0.0% |
| query.builder_filter_sum_large | 144.248 | 153.433 | -3.1% | 29.1% | ~ inconclusive | 93.3 KiB | 93.3 KiB | -+0.0% |
| query.groupby_category | 119.927 | 117.573 | -3.0% | 46.6% | ~ inconclusive | 218.0 KiB | 218.0 KiB | +0.0% |
| query.expr_groupby_orderby_limit | 135.793 | 146.433 | +7.8% | 17.2% | ~ inconclusive | 755.4 KiB | 755.3 KiB | -+0.0% |
| query.rows_orderby_limit | 118.125 | 117.435 | +0.7% | 16.8% | ~ inconclusive | 328.4 KiB | 328.4 KiB | +0.0% |
| query.rows_limit_early_exit | 1.940 | 1.435 | -23.0% | 15.8% | faster | 239.3 KiB | 239.3 KiB | -+0.0% |
| query.distinct_product | 131.777 | 124.843 | -5.3% | 25.7% | ~ inconclusive | 735.1 KiB | 735.1 KiB | +0.0% |
