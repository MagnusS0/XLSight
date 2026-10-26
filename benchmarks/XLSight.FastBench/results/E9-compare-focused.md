base: ab-base-1 rev 172e37b740c1 dirty clean-base-worktree XLSight.dll ca0bb0dff152 (complete, 4112 ms); ab-base-2 rev 172e37b740c1 dirty clean-base-worktree XLSight.dll ca0bb0dff152 (complete, 4118 ms); ab-base-3 rev 172e37b740c1 dirty clean-base-worktree XLSight.dll ca0bb0dff152 (complete, 3996 ms); ab-base-4 rev 172e37b740c1 dirty clean-base-worktree XLSight.dll ca0bb0dff152 (complete, 4063 ms); ab-base-5 rev 172e37b740c1 dirty clean-base-worktree XLSight.dll ca0bb0dff152 (complete, 4147 ms)
cand: ab-cand-1 rev 172e37b740c1 dirty src-diff-39da51fa8249 XLSight.dll ca0bb0dff152 (complete, 4029 ms); ab-cand-2 rev 172e37b740c1 dirty src-diff-39da51fa8249 XLSight.dll ca0bb0dff152 (complete, 4328 ms); ab-cand-3 rev 172e37b740c1 dirty src-diff-39da51fa8249 XLSight.dll ca0bb0dff152 (complete, 4209 ms); ab-cand-4 rev 172e37b740c1 dirty src-diff-39da51fa8249 XLSight.dll ca0bb0dff152 (complete, 4122 ms); ab-cand-5 rev 172e37b740c1 dirty src-diff-39da51fa8249 XLSight.dll ca0bb0dff152 (complete, 4038 ms)
mode: paired interleaved rounds (median of per-round deltas; verdict needs every round to agree in sign)

| case | base ms | cand ms | Δ time | noise ± | verdict | base alloc/op | cand alloc/op | Δ alloc |
|---|--:|--:|--:|--:|---|--:|--:|--:|
| scan.real_mcdonalds_all | 126.798 | 127.676 | -14.5% | 26.5% | ~ inconclusive | 763.6 KiB | 763.6 KiB | +0.0% |
| layout.complex_calculator | 2.067 | 2.019 | +1.7% | 13.2% | ~ inconclusive | 286.1 KiB | 286.4 KiB | +0.1% |
| layout.mcdonalds_all | 272.338 | 252.698 | -4.7% | 33.4% | ~ inconclusive | 5.5 MiB | 5.5 MiB | +0.0% |
| layout.sparse_fragmented_2k | 150.343 | 144.567 | -5.5% | 63.1% | ~ inconclusive | 5.3 MiB | 5.3 MiB | -0.3% |
