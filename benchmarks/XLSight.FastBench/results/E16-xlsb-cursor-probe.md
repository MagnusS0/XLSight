# E16 XLSB cursor probe (wave-2 Memory worker `xlsbcursorB.cs`, re-run by the lead)

Base = 73a1400 tree (`artifacts/probe-base`), cand = working tree with `w2-memory-2.patch`.

## Allocation (deterministic, two runs each, identical)

| Case | Base | Cand |
|---|--:|--:|
| First unbounded cursor in a fresh process (open + first row) | 1024 KiB | 6 KiB |
| 4 unbounded readers open at once | 4256 KiB | 178 KiB |
| Bounded A1:E10 | 1 KiB | 1 KiB |

## Full scan large.xlsb (500,005 cells), 3 alternating rounds, load average 2.6–5

| Round | Base median / min | Cand median / min |
|---|--:|--:|
| 1 | 147.36 / 145.30 ms | 159.79 / 151.51 ms |
| 2 | 156.34 / 144.69 ms | 151.62 / 148.64 ms |
| 3 | 159.63 / 149.71 ms | 153.57 / 149.81 ms |
