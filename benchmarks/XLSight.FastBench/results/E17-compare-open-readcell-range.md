base: ab-base-1 rev aa90509a1f36 dirty clean-base-worktree XLSight.dll fc6fc8120e7f (complete, 3640 ms); ab-base-2 rev aa90509a1f36 dirty clean-base-worktree XLSight.dll fc6fc8120e7f (complete, 3490 ms); ab-base-3 rev aa90509a1f36 dirty clean-base-worktree XLSight.dll fc6fc8120e7f (complete, 3366 ms); ab-base-4 rev aa90509a1f36 dirty clean-base-worktree XLSight.dll fc6fc8120e7f (complete, 3500 ms); ab-base-5 rev aa90509a1f36 dirty clean-base-worktree XLSight.dll fc6fc8120e7f (complete, 3457 ms)
cand: ab-cand-1 rev aa90509a1f36 dirty src-diff-767d9edd7d2b XLSight.dll 9d4e2cd0ca3a (complete, 3494 ms); ab-cand-2 rev aa90509a1f36 dirty src-diff-767d9edd7d2b XLSight.dll 9d4e2cd0ca3a (complete, 3350 ms); ab-cand-3 rev aa90509a1f36 dirty src-diff-767d9edd7d2b XLSight.dll 9d4e2cd0ca3a (complete, 3476 ms); ab-cand-4 rev aa90509a1f36 dirty src-diff-767d9edd7d2b XLSight.dll 9d4e2cd0ca3a (complete, 3370 ms); ab-cand-5 rev aa90509a1f36 dirty src-diff-767d9edd7d2b XLSight.dll 9d4e2cd0ca3a (complete, 3443 ms)
mode: paired interleaved rounds (median of per-round deltas; verdict needs every round to agree in sign)

| case | base ms | cand ms | Δ time | noise ± | verdict | base alloc/op | cand alloc/op | Δ alloc |
|---|--:|--:|--:|--:|---|--:|--:|--:|
| open.small | 0.161 | 0.115 | -27.9% | 15.4% | faster | 53.9 KiB | 53.9 KiB | +0.0% |
| open.banking_real | 8.160 | 7.634 | +4.3% | 23.3% | ~ inconclusive | 594.2 KiB | 594.2 KiB | +0.0% |
| readcell.large_early_exit | 0.581 | 0.584 | +0.7% | 12.9% | ~ inconclusive | 87.1 KiB | 87.1 KiB | +0.0% |
| range.large_middle_rows | 44.205 | 45.172 | +2.2% | 4.1% | ~ inconclusive | 87.5 KiB | 87.5 KiB | +0.0% |
| range.readrange_medium | 26.825 | 22.851 | -17.1% | 26.7% | ~ inconclusive | 456.9 KiB | 456.9 KiB | +0.0% |
| range.async_large_middle_rows | 97.501 | 86.582 | -7.3% | 12.3% | ~ inconclusive | 215.4 KiB | 214.4 KiB | -0.4% |
| query.rows_limit_early_exit | 4.863 | 4.848 | +1.9% | 38.5% | ~ inconclusive | 239.3 KiB | 239.3 KiB | +0.0% |
