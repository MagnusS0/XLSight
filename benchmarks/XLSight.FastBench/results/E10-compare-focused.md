base: ab-base-1 rev 39a10a8dc953 dirty clean-base-worktree XLSight.dll 9690a8678d6b (complete, 5672 ms); ab-base-2 rev 39a10a8dc953 dirty clean-base-worktree XLSight.dll 9690a8678d6b (complete, 5528 ms); ab-base-3 rev 39a10a8dc953 dirty clean-base-worktree XLSight.dll 9690a8678d6b (complete, 5757 ms)
cand: ab-cand-1 rev 39a10a8dc953 dirty src-diff-5350a9afd407 XLSight.dll 9690a8678d6b (complete, 5046 ms); ab-cand-2 rev 39a10a8dc953 dirty src-diff-5350a9afd407 XLSight.dll 9690a8678d6b (complete, 5430 ms); ab-cand-3 rev 39a10a8dc953 dirty src-diff-5350a9afd407 XLSight.dll 9690a8678d6b (complete, 5176 ms)
mode: paired interleaved rounds (median of per-round deltas; verdict needs every round to agree in sign)

| case | base ms | cand ms | Δ time | noise ± | verdict | base alloc/op | cand alloc/op | Δ alloc |
|---|--:|--:|--:|--:|---|--:|--:|--:|
| analyze.full_banking_real | 184.852 | 188.374 | +14.2% | 129.0% | ~ inconclusive | 3.9 MiB | 3.7 MiB | -3.2% |
| analyze.observed_strings_repeated | 230.136 | 217.446 | -1.6% | 27.0% | ~ inconclusive | 596.6 KiB | 596.6 KiB | +0.0% |
| analyze.full_merged_regions | 8.417 | 8.051 | +0.4% | 18.0% | ~ inconclusive | 479.9 KiB | 479.9 KiB | +0.0% |
| layout.complex_calculator | 1.857 | 1.496 | -20.8% | 18.8% | ~ inconclusive | 286.7 KiB | 288.4 KiB | +0.6% |
| layout.mcdonalds_all | 228.927 | 245.746 | +7.3% | 24.5% | ~ inconclusive | 5.5 MiB | 5.5 MiB | +0.7% |
| layout.sparse_fragmented_2k | 127.881 | 99.900 | -28.9% | 25.8% | faster | 5.3 MiB | 5.3 MiB | -1.3% |
