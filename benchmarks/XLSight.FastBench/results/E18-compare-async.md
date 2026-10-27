base: ab-base-1 rev 8ea02df47124 dirty clean-base-worktree XLSight.dll 0f5236aafe48 (complete, 1862 ms); ab-base-2 rev 8ea02df47124 dirty clean-base-worktree XLSight.dll 0f5236aafe48 (complete, 2025 ms); ab-base-3 rev 8ea02df47124 dirty clean-base-worktree XLSight.dll 0f5236aafe48 (complete, 1912 ms); ab-base-4 rev 8ea02df47124 dirty clean-base-worktree XLSight.dll 0f5236aafe48 (complete, 1919 ms); ab-base-5 rev 8ea02df47124 dirty clean-base-worktree XLSight.dll 0f5236aafe48 (complete, 1928 ms)
cand: ab-cand-1 rev 8ea02df47124 dirty src-diff-36264ea31074 XLSight.dll 97176edb5835 (complete, 1904 ms); ab-cand-2 rev 8ea02df47124 dirty src-diff-36264ea31074 XLSight.dll 97176edb5835 (complete, 1932 ms); ab-cand-3 rev 8ea02df47124 dirty src-diff-36264ea31074 XLSight.dll 97176edb5835 (complete, 1854 ms); ab-cand-4 rev 8ea02df47124 dirty src-diff-36264ea31074 XLSight.dll 97176edb5835 (complete, 1971 ms); ab-cand-5 rev 8ea02df47124 dirty src-diff-36264ea31074 XLSight.dll 97176edb5835 (complete, 1870 ms)
mode: paired interleaved rounds (median of per-round deltas; verdict needs every round to agree in sign)

| case | base ms | cand ms | Δ time | noise ± | verdict | base alloc/op | cand alloc/op | Δ alloc |
|---|--:|--:|--:|--:|---|--:|--:|--:|
| scan.numeric_xlsx | 118.836 | 116.223 | +1.1% | 5.9% | ~ inconclusive | 87.5 KiB | 87.6 KiB | +0.0% |
| range.async_large_middle_rows | 41.773 | 34.286 | -18.4% | 14.6% | faster | 202.0 KiB | 109.7 KiB | -45.7% |
