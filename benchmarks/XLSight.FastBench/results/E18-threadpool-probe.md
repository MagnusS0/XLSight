# E18 thread-pool probe (lead `tp.cs`)

Full `GetSheetReaderAsync` + `ReadAsync` scan of large.xlsx (500,005 cells), with
`ConfigureAwait(false)` throughout. 9 iterations, the first 3 dropped. Base = 73a1400 tree,
cand = working tree.

| Run | Pool items/op | Alloc/op | Median | Min |
|---|--:|--:|--:|--:|
| base, run 1 | 463.17 | 26,562 KiB | 537–553 ms | 503–543 ms |
| cand, run 1 | 1.00 | 26,381 KiB | 457–460 ms | 441–449 ms |
| base, run 2 | 463.00 | 26,563 KiB | 561 ms | 531 ms |
| cand, run 2 | 1.00 | 26,381 KiB | 482 ms | 450 ms |

The remaining item is the `Task.Run` that `GetSheetReaderAsync` already uses to open the cursor.
With a `SynchronizationContext` installed before the call, the counts are the same, because that
`Task.Run` and `ConfigureAwait(false)` already move the scan onto the pool.

The case the guard covers is a caller that resumes on its context between `ReadAsync` calls.
There, refills take the inner `ReadAsync` path exactly as before. The unit test
`ReadAsync_InlinesOnlyWithoutSynchronizationContext` pins this.
