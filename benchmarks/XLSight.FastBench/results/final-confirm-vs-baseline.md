base: confirm-base rev 5643a0cc2dad dirty clean-base-worktree XLSight.dll ddedb92cd694 (complete, 51282 ms); confirm-base2 rev 5643a0cc2dad dirty clean-base-worktree XLSight.dll ddedb92cd694 (complete, 49950 ms)
cand: confirm rev afe3c50d4fb5 dirty clean XLSight.dll afcc12f9b82f (complete, 45824 ms); confirm-cand2 rev afe3c50d4fb5 dirty clean XLSight.dll afcc12f9b82f (complete, 45575 ms)
mode: paired interleaved rounds (median of per-round deltas; verdict needs every round to agree in sign)

| case | base ms | cand ms | Δ time | noise ± | verdict | base alloc/op | cand alloc/op | Δ alloc |
|---|--:|--:|--:|--:|---|--:|--:|--:|
| confirm.cold_scan_strings_repeated_x10 | 1065.776 | 844.600 | -20.8% | 3.0% | single-op (see RSS) | 941.8 KiB | 750.0 KiB | -20.4% |
| confirm.cold_scan_strings_repeated_x10 peak RSS | 56.0 MiB | 56.0 MiB | -+0.0% | | | | | |
| confirm.cold_stream_snapshots_mixed_x8 | 982.685 | 917.953 | -6.6% | 3.0% | single-op (see RSS) | 145.9 MiB | 131.1 MiB | -10.1% |
| confirm.cold_stream_snapshots_mixed_x8 peak RSS | 73.2 MiB | 72.7 MiB | -0.7% | | | | | |
| confirm.cold_analyze_full_strings_repeated_x10 | 1978.905 | 1514.508 | -23.5% | 3.0% | single-op (see RSS) | 1.3 MiB | 932.8 KiB | -29.1% |
| confirm.cold_analyze_full_strings_repeated_x10 peak RSS | 58.8 MiB | 59.5 MiB | +1.2% | | | | | |
| confirm.cold_query_groupby_x10 | 1197.529 | 931.562 | -22.2% | 3.0% | single-op (see RSS) | 1.4 MiB | 1.2 MiB | -13.6% |
| confirm.cold_query_groupby_x10 peak RSS | 58.2 MiB | 57.7 MiB | -0.8% | | | | | |
| confirm.warm_scan_numeric_xlsx | 145.204 | 113.851 | -21.6% | 3.0% | faster? (unpaired; use ab) | 279.7 KiB | 87.6 KiB | -68.7% |
| confirm.warm_scan_strings_repeated_x10 | 1074.523 | 874.600 | -18.6% | 3.0% | faster? (unpaired; use ab) | 506.0 KiB | 306.0 KiB | -39.5% |
| confirm.warm_scan_mixed_sparse_x8 | 963.953 | 869.666 | -9.8% | 3.0% | faster? (unpaired; use ab) | 34.3 MiB | 19.6 MiB | -42.9% |
