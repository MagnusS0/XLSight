# E17 ReadCell probe: cell that needs no shared string (lead probe `rc.cs`)

Each iteration opens the workbook, then times `ReadCell` alone. 700 iterations, the first 100
dropped. Base = 73a1400 tree, cand = working tree with the deferred SST start. Three alternating
process rounds; load average 2.8–4.

| Case | Base alloc | Cand alloc | Base p10 (3 rounds) | Cand p10 (3 rounds) |
|---|--:|--:|--:|--:|
| mixed_sparse A1 (date) | 98.1 KiB | 32.3 KiB | 466 / 450 / 453 µs | 422 / 422 / 458 µs |
| strings_repeated A2 (number) | 112.7 KiB | 32.3 KiB | 939 / 587 / 592 µs | 920 / 482 / 725 µs |
| mixed_sparse B1 (empty) | 98.2 KiB | 32.4 KiB | 368 / 382 / 385 µs | 343 / 356 / 337 µs |

The worker's probe (`w2-memory/probe/h3b`) gave the same allocation figures, plus strings_unique Z1
160.8 → 32.3 KiB. Its time for mixed_sparse rose (430 → 720 µs), but that probe interleaves
internal-API stage measurements that differ between the two trees. This probe does not reproduce
the rise.
