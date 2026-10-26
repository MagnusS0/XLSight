base: ab-base-1 rev 012d17b64942 dirty clean-base-worktree XLSight.dll fa5b48c89007 (complete, 19934 ms); ab-base-2 rev 012d17b64942 dirty clean-base-worktree XLSight.dll fa5b48c89007 (complete, 20178 ms); ab-base-3 rev 012d17b64942 dirty clean-base-worktree XLSight.dll fa5b48c89007 (complete, 20753 ms)
cand: ab-cand-1 rev 012d17b64942 dirty src-diff-739228bb8a8d XLSight.dll 3866b9cabecf (complete, 19838 ms); ab-cand-2 rev 012d17b64942 dirty src-diff-739228bb8a8d XLSight.dll 3866b9cabecf (complete, 20646 ms); ab-cand-3 rev 012d17b64942 dirty src-diff-739228bb8a8d XLSight.dll 3866b9cabecf (complete, 20235 ms)
mode: paired interleaved rounds (median of per-round deltas; verdict needs every round to agree in sign)

| case | base ms | cand ms | Δ time | noise ± | verdict | base alloc/op | cand alloc/op | Δ alloc |
|---|--:|--:|--:|--:|---|--:|--:|--:|
| mem.cold_scan_strings_unique | 315.370 | 305.381 | -3.2% | 3.0% | single-op (see RSS) | 20.3 MiB | 20.1 MiB | -1.0% |
| mem.cold_scan_strings_unique peak RSS | 75.6 MiB | 75.2 MiB | -0.5% | | | | | |
| mem.cold_analyze_banking_real | 250.209 | 272.199 | +8.8% | 3.0% | single-op (see RSS) | 5.3 MiB | 4.4 MiB | -16.9% |
| mem.cold_analyze_banking_real peak RSS | 64.2 MiB | 63.3 MiB | -1.4% | | | | | |
| open.small | 0.178 | 0.167 | -7.4% | 23.3% | ~ inconclusive | 182.0 KiB | 53.9 KiB | -70.4% |
| open.banking_real | 6.708 | 6.869 | +2.4% | 8.3% | ~ inconclusive | 658.3 KiB | 594.2 KiB | -9.7% |
| readcell.large_early_exit | 0.616 | 0.552 | -10.6% | 19.9% | ~ inconclusive | 279.2 KiB | 87.1 KiB | -68.8% |
| scan.numeric_xlsx | 154.899 | 150.367 | -5.2% | 8.2% | ~ inconclusive | 280.2 KiB | 88.1 KiB | -68.6% |
| scan.numeric_xlsb | 48.834 | 47.946 | -3.8% | 5.9% | ~ inconclusive | 28.6 KiB | 28.6 KiB | +0.0% |
| scan.strings_xlsb | 1.137 | 1.245 | +4.6% | 15.1% | ~ inconclusive | 54.7 KiB | 54.7 KiB | +0.0% |
| scan.strings_repeated | 120.786 | 122.269 | +1.2% | 4.8% | ~ inconclusive | 506.3 KiB | 314.2 KiB | -37.9% |
| scan.strings_unique | 116.770 | 110.874 | -5.9% | 23.9% | ~ inconclusive | 19.9 MiB | 19.7 MiB | -0.9% |
| scan.mixed_sparse_values | 126.957 | 133.702 | +3.9% | 10.4% | ~ inconclusive | 4.6 MiB | 4.4 MiB | -4.1% |
| scan.mixed_sparse_formulas | 152.447 | 152.201 | -1.4% | 3.0% | ~ inconclusive | 9.3 MiB | 9.1 MiB | -2.0% |
| stream.snapshots_mixed | 133.233 | 131.954 | -1.5% | 4.1% | ~ inconclusive | 18.5 MiB | 18.3 MiB | -1.0% |
| scan.real_mcdonalds_all | 101.528 | 99.856 | +0.6% | 8.5% | ~ inconclusive | 844.2 KiB | 780.1 KiB | -7.6% |
| range.large_middle_rows | 50.606 | 51.273 | -3.4% | 19.6% | ~ inconclusive | 279.8 KiB | 87.7 KiB | -68.6% |
| range.readrange_medium | 4.847 | 4.772 | -1.4% | 17.7% | ~ inconclusive | 649.1 KiB | 457.1 KiB | -29.6% |
| range.async_large_middle_rows | 55.254 | 56.192 | +3.5% | 11.1% | ~ inconclusive | 397.1 KiB | 205.8 KiB | -48.2% |
| analyze.full_banking_real | 87.199 | 77.243 | -11.4% | 28.7% | ~ inconclusive | 4.6 MiB | 3.7 MiB | -19.3% |
| analyze.observed_strings_repeated | 201.183 | 192.339 | -6.9% | 17.6% | ~ inconclusive | 860.9 KiB | 604.8 KiB | -29.7% |
| analyze.full_merged_regions | 178.558 | 181.300 | +0.9% | 6.3% | ~ inconclusive | 744.8 KiB | 488.7 KiB | -34.4% |
| layout.complex_calculator | 1.401 | 1.381 | -1.3% | 5.3% | ~ inconclusive | 481.6 KiB | 299.6 KiB | -37.8% |
| layout.mcdonalds_all | 195.029 | 169.939 | -13.4% | 8.9% | faster | 6.0 MiB | 6.0 MiB | -1.0% |
| layout.sparse_fragmented_2k | 103.498 | 102.960 | -0.5% | 9.2% | ~ inconclusive | 6.6 MiB | 6.4 MiB | -2.9% |
| query.builder_filter_sum_large | 149.348 | 158.617 | -2.6% | 17.9% | ~ inconclusive | 285.4 KiB | 93.3 KiB | -67.3% |
| query.groupby_category | 122.974 | 123.308 | +7.5% | 40.7% | ~ inconclusive | 418.1 KiB | 226.0 KiB | -45.9% |
| query.expr_groupby_orderby_limit | 133.291 | 138.351 | +3.8% | 8.5% | ~ inconclusive | 955.4 KiB | 763.3 KiB | -20.1% |
| query.rows_orderby_limit | 114.521 | 116.971 | +2.1% | 35.6% | ~ inconclusive | 528.5 KiB | 336.4 KiB | -36.3% |
| query.rows_limit_early_exit | 1.449 | 1.410 | -1.1% | 17.4% | ~ inconclusive | 439.4 KiB | 247.3 KiB | -43.7% |
| query.distinct_product | 116.063 | 133.685 | +5.0% | 9.5% | ~ inconclusive | 935.1 KiB | 743.0 KiB | -20.5% |
