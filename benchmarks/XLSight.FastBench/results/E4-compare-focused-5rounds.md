base: ab-base-1 rev 16976e42826d dirty clean-base-worktree XLSight.dll c6da1ee76991 (complete, 9670 ms); ab-base-2 rev 16976e42826d dirty clean-base-worktree XLSight.dll c6da1ee76991 (complete, 10160 ms); ab-base-3 rev 16976e42826d dirty clean-base-worktree XLSight.dll c6da1ee76991 (complete, 9735 ms); ab-base-4 rev 16976e42826d dirty clean-base-worktree XLSight.dll c6da1ee76991 (complete, 9705 ms); ab-base-5 rev 16976e42826d dirty clean-base-worktree XLSight.dll c6da1ee76991 (complete, 9818 ms)
cand: ab-cand-1 rev 16976e42826d dirty src-diff-74105ec37350 XLSight.dll 0e24b4827103 (complete, 8832 ms); ab-cand-2 rev 16976e42826d dirty src-diff-74105ec37350 XLSight.dll 0e24b4827103 (complete, 9184 ms); ab-cand-3 rev 16976e42826d dirty src-diff-74105ec37350 XLSight.dll 0e24b4827103 (complete, 9035 ms); ab-cand-4 rev 16976e42826d dirty src-diff-74105ec37350 XLSight.dll 0e24b4827103 (complete, 9031 ms); ab-cand-5 rev 16976e42826d dirty src-diff-74105ec37350 XLSight.dll 0e24b4827103 (complete, 9228 ms)
mode: paired interleaved rounds (median of per-round deltas; verdict needs every round to agree in sign)

| case | base ms | cand ms | Δ time | noise ± | verdict | base alloc/op | cand alloc/op | Δ alloc |
|---|--:|--:|--:|--:|---|--:|--:|--:|
| scan.numeric_xlsx | 163.861 | 138.182 | -16.9% | 15.3% | faster | 87.5 KiB | 87.6 KiB | +0.0% |
| scan.strings_repeated | 122.324 | 124.187 | +2.5% | 28.3% | ~ inconclusive | 306.1 KiB | 306.1 KiB | +0.0% |
| scan.mixed_sparse_values | 141.125 | 135.092 | -4.6% | 15.0% | ~ inconclusive | 4.4 MiB | 4.4 MiB | -+0.0% |
| scan.real_mcdonalds_all | 96.348 | 100.230 | -+0.0% | 13.0% | ~ inconclusive | 772.1 KiB | 772.1 KiB | +0.0% |
| confirm.warm_scan_numeric_xlsx | 152.476 | 137.107 | -10.1% | 6.2% | faster | 87.7 KiB | 87.7 KiB | +0.0% |
