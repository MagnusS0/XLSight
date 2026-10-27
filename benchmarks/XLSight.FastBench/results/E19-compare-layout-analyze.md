base: ab-base-1 rev 2dae95f38481 dirty clean-base-worktree XLSight.dll 792ea78e7499 (complete, 4686 ms); ab-base-2 rev 2dae95f38481 dirty clean-base-worktree XLSight.dll 792ea78e7499 (complete, 4727 ms); ab-base-3 rev 2dae95f38481 dirty clean-base-worktree XLSight.dll 792ea78e7499 (complete, 4585 ms); ab-base-4 rev 2dae95f38481 dirty clean-base-worktree XLSight.dll 792ea78e7499 (complete, 4539 ms); ab-base-5 rev 2dae95f38481 dirty clean-base-worktree XLSight.dll 792ea78e7499 (complete, 4564 ms)
cand: ab-cand-1 rev 2dae95f38481 dirty src-diff-6299e05e9380 XLSight.dll 792ea78e7499 (complete, 4659 ms); ab-cand-2 rev 2dae95f38481 dirty src-diff-6299e05e9380 XLSight.dll 792ea78e7499 (complete, 4678 ms); ab-cand-3 rev 2dae95f38481 dirty src-diff-6299e05e9380 XLSight.dll 792ea78e7499 (complete, 4782 ms); ab-cand-4 rev 2dae95f38481 dirty src-diff-6299e05e9380 XLSight.dll 792ea78e7499 (complete, 4608 ms); ab-cand-5 rev 2dae95f38481 dirty src-diff-6299e05e9380 XLSight.dll 792ea78e7499 (complete, 4763 ms)
mode: paired interleaved rounds (median of per-round deltas; verdict needs every round to agree in sign)

| case | base ms | cand ms | Δ time | noise ± | verdict | base alloc/op | cand alloc/op | Δ alloc |
|---|--:|--:|--:|--:|---|--:|--:|--:|
| analyze.full_banking_real | 156.138 | 158.817 | +4.1% | 91.1% | ~ inconclusive | 3.8 MiB | 3.8 MiB | -1.6% |
| analyze.observed_strings_repeated | 192.507 | 192.005 | +0.3% | 5.7% | ~ inconclusive | 469.4 KiB | 469.4 KiB | +0.0% |
| analyze.full_merged_regions | 6.875 | 6.675 | -1.0% | 5.8% | ~ inconclusive | 477.8 KiB | 477.8 KiB | +0.0% |
| layout.complex_calculator | 1.573 | 1.598 | -3.8% | 8.6% | ~ inconclusive | 288.5 KiB | 289.6 KiB | +0.4% |
| layout.mcdonalds_all | 206.721 | 201.197 | +1.5% | 18.2% | ~ inconclusive | 5.6 MiB | 5.6 MiB | +0.2% |
| layout.sparse_fragmented_2k | 76.814 | 85.087 | +12.9% | 21.6% | ~ inconclusive | 5.3 MiB | 5.3 MiB | +0.4% |
