# Allocation profile: proxy1m.xlsx / Worksheet

Runtime 10.0.13, workstation GC, latency Batch. Each row is a fresh process.

| mode | rows | cells | elapsed ms | alloc MiB | alloc B/cell | gen0/1/2 | GC pause ms | GC pause % | peak RSS MiB |
|---|--:|--:|--:|--:|--:|---|--:|--:|--:|
| reader | 985001 | 9554492 | 2507 | 124.2 | 13.6 | 0/0/0 | 0 | 0.0 | 168.4 |
| stream | 985001 | 9554492 | 2975 | 365.4 | 40.1 | 2/1/0 | 137 | 4.6 | 272.6 |

## Cells

Text 5614492, Number 2955000, Date 985000

## Strings the borrowed reader materializes (estimated object sizes, 64-bit)

| source | accesses | distinct | est. MiB |
|---|--:|--:|--:|
| shared strings, index < 131072 (decoded once, cached) | 4246178 | 131072 | 8.0 |
| shared strings, index ≥ 131072 (decoded on every access) | 1368314 | 1171259 | 83.1 |
| inline strings, formula strings, errors | 0 | — | 0.0 |

Repeat decodes of uncached entries: 197055 accesses, ~12.0 MiB at the mean uncached size. This is the most any cache over the uncached range could save.

Uncached reuse (accesses per entry → entries / accesses): 1: 1088044 / 1088044, 2: 45699 / 91398, 3–4: 23927 / 79400, 5–16: 12885 / 93483, >16: 704 / 15989

Allocation of this accounting pass (includes its own dictionaries): 180.2 MiB.
Compare the reader row above against the string total: the remainder is the shared-string table itself (UTF-8 arena and index) plus anything else.