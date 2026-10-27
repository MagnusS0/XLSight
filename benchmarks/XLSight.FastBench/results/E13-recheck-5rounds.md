base: ab-base-1 rev 73a14006be64 dirty clean-base-worktree XLSight.dll f33470fdcd6b (complete, 6971 ms); ab-base-2 rev 73a14006be64 dirty clean-base-worktree XLSight.dll f33470fdcd6b (complete, 7020 ms); ab-base-3 rev 73a14006be64 dirty clean-base-worktree XLSight.dll f33470fdcd6b (complete, 7281 ms); ab-base-4 rev 73a14006be64 dirty clean-base-worktree XLSight.dll f33470fdcd6b (complete, 7046 ms); ab-base-5 rev 73a14006be64 dirty clean-base-worktree XLSight.dll f33470fdcd6b (complete, 7321 ms)
cand: ab-cand-1 rev 73a14006be64 dirty src-diff-23f6eadf5f17 XLSight.dll e1ca1832435a (complete, 7315 ms); ab-cand-2 rev 73a14006be64 dirty src-diff-23f6eadf5f17 XLSight.dll e1ca1832435a (complete, 7031 ms); ab-cand-3 rev 73a14006be64 dirty src-diff-23f6eadf5f17 XLSight.dll e1ca1832435a (complete, 6844 ms); ab-cand-4 rev 73a14006be64 dirty src-diff-23f6eadf5f17 XLSight.dll e1ca1832435a (complete, 6828 ms); ab-cand-5 rev 73a14006be64 dirty src-diff-23f6eadf5f17 XLSight.dll e1ca1832435a (complete, 6994 ms)
mode: paired interleaved rounds (median of per-round deltas; verdict needs every round to agree in sign)

| case | base ms | cand ms | Δ time | noise ± | verdict | base alloc/op | cand alloc/op | Δ alloc |
|---|--:|--:|--:|--:|---|--:|--:|--:|
| scan.numeric_xlsx | 131.963 | 123.095 | -8.1% | 9.5% | ~ inconclusive | 87.5 KiB | 87.6 KiB | +0.0% |
| scan.numeric_xlsb | 93.302 | 95.325 | -2.2% | 62.7% | ~ inconclusive | 28.6 KiB | 28.6 KiB | +0.0% |
| scan.strings_xlsb | 1.449 | 1.411 | -0.3% | 8.5% | ~ inconclusive | 54.7 KiB | 54.7 KiB | +0.0% |
| scan.strings_repeated | 117.462 | 98.327 | -17.4% | 25.5% | ~ inconclusive | 306.1 KiB | 306.0 KiB | -+0.0% |
| scan.strings_unique | 110.777 | 119.089 | +7.5% | 35.7% | ~ inconclusive | 19.7 MiB | 19.7 MiB | +0.0% |
| scan.mixed_sparse_values | 118.454 | 118.549 | +2.2% | 25.5% | ~ inconclusive | 2.6 MiB | 2.6 MiB | -+0.0% |
| scan.mixed_sparse_formulas | 167.768 | 165.478 | -3.2% | 25.2% | ~ inconclusive | 5.1 MiB | 5.1 MiB | +0.0% |
| stream.snapshots_mixed | 127.500 | 123.537 | -3.7% | 6.7% | ~ inconclusive | 16.5 MiB | 16.5 MiB | -+0.0% |
| scan.real_mcdonalds_all | 97.942 | 84.678 | -8.5% | 19.4% | ~ inconclusive | 764.2 KiB | 764.2 KiB | +0.0% |
| query.groupby_category | 129.446 | 124.706 | +8.2% | 27.6% | ~ inconclusive | 217.9 KiB | 217.9 KiB | -+0.0% |
