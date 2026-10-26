base: ab-base-1 rev 27fc006a4066 dirty clean-base-worktree XLSight.dll b34f4bae5b02 (complete, 3741 ms); ab-base-2 rev 27fc006a4066 dirty clean-base-worktree XLSight.dll b34f4bae5b02 (complete, 3678 ms); ab-base-3 rev 27fc006a4066 dirty clean-base-worktree XLSight.dll b34f4bae5b02 (complete, 3810 ms); ab-base-4 rev 27fc006a4066 dirty clean-base-worktree XLSight.dll b34f4bae5b02 (complete, 4349 ms); ab-base-5 rev 27fc006a4066 dirty clean-base-worktree XLSight.dll b34f4bae5b02 (complete, 3885 ms)
cand: ab-cand-1 rev 27fc006a4066 dirty src-diff-3f082e330d44 XLSight.dll 58ed9be1f5be (complete, 4244 ms); ab-cand-2 rev 27fc006a4066 dirty src-diff-3f082e330d44 XLSight.dll 58ed9be1f5be (complete, 3763 ms); ab-cand-3 rev 27fc006a4066 dirty src-diff-3f082e330d44 XLSight.dll 58ed9be1f5be (complete, 3757 ms); ab-cand-4 rev 27fc006a4066 dirty src-diff-3f082e330d44 XLSight.dll 58ed9be1f5be (complete, 4243 ms); ab-cand-5 rev 27fc006a4066 dirty src-diff-3f082e330d44 XLSight.dll 58ed9be1f5be (complete, 4364 ms)
mode: paired interleaved rounds (median of per-round deltas; verdict needs every round to agree in sign)

| case | base ms | cand ms | Δ time | noise ± | verdict | base alloc/op | cand alloc/op | Δ alloc |
|---|--:|--:|--:|--:|---|--:|--:|--:|
| analyze.full_banking_real | 180.138 | 214.905 | -2.1% | 87.6% | ~ inconclusive | 3.9 MiB | 3.8 MiB | -1.0% |
| analyze.observed_strings_repeated | 214.078 | 202.602 | -4.5% | 6.4% | ~ inconclusive | 596.6 KiB | 469.3 KiB | -21.3% |
| query.groupby_category | 202.230 | 201.763 | -0.2% | 17.5% | ~ inconclusive | 218.0 KiB | 217.9 KiB | -0.1% |
