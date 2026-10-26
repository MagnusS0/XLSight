base: ab-base-1 rev 2f7d50f1757a dirty clean-base-worktree XLSight.dll 448d8a428ab4 (complete, 3940 ms); ab-base-2 rev 2f7d50f1757a dirty clean-base-worktree XLSight.dll 448d8a428ab4 (complete, 4450 ms); ab-base-3 rev 2f7d50f1757a dirty clean-base-worktree XLSight.dll 448d8a428ab4 (complete, 4079 ms); ab-base-4 rev 2f7d50f1757a dirty clean-base-worktree XLSight.dll 448d8a428ab4 (complete, 3987 ms); ab-base-5 rev 2f7d50f1757a dirty clean-base-worktree XLSight.dll 448d8a428ab4 (complete, 4141 ms)
cand: ab-cand-1 rev 2f7d50f1757a dirty src-diff-717ef5d4abdd XLSight.dll 80d33c1aeec1 (complete, 3404 ms); ab-cand-2 rev 2f7d50f1757a dirty src-diff-717ef5d4abdd XLSight.dll 80d33c1aeec1 (complete, 3533 ms); ab-cand-3 rev 2f7d50f1757a dirty src-diff-717ef5d4abdd XLSight.dll 80d33c1aeec1 (complete, 3529 ms); ab-cand-4 rev 2f7d50f1757a dirty src-diff-717ef5d4abdd XLSight.dll 80d33c1aeec1 (complete, 3316 ms); ab-cand-5 rev 2f7d50f1757a dirty src-diff-717ef5d4abdd XLSight.dll 80d33c1aeec1 (complete, 3352 ms)
mode: paired interleaved rounds (median of per-round deltas; verdict needs every round to agree in sign)

| case | base ms | cand ms | Δ time | noise ± | verdict | base alloc/op | cand alloc/op | Δ alloc |
|---|--:|--:|--:|--:|---|--:|--:|--:|
| scan.numeric_xlsx | 136.375 | 135.702 | -3.1% | 9.7% | ~ inconclusive | 87.5 KiB | 87.5 KiB | +0.0% |
| scan.strings_unique | 161.769 | 180.809 | +3.5% | 51.4% | ~ inconclusive | 19.7 MiB | 19.7 MiB | +0.0% |
| scan.mixed_sparse_values | 129.866 | 124.680 | -6.9% | 8.5% | ~ inconclusive | 2.6 MiB | 2.6 MiB | +0.0% |
| analyze.full_merged_regions | 189.573 | 9.803 | -94.9% | 6.2% | faster | 480.5 KiB | 480.0 KiB | -0.1% |
