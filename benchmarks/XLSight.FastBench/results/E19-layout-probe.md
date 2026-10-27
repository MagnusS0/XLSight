# E19 layout probe: AnalyzeLayout min/median of 3 after warmup, two alternating process rounds

Base = 73a1400 tree, cand = working tree with `w2-query-2.patch`. Probe: lead `layoutprobe/template.cs`. Generated sheets (stack*, sparse*, t60k) are from the wave-1 Query worker.

```
== base
stack1600.xlsx         min     235.9 ms  median     277.7 ms  alloc    11.6 MiB
stack3200.xlsx         min     512.8 ms  median     531.7 ms  alloc    23.0 MiB
sparse6250.xlsx        min     531.1 ms  median     562.5 ms  alloc    16.2 MiB
sparse12500.xlsx       min    1515.5 ms  median    1546.4 ms  alloc    32.6 MiB
mixed_sparse.xlsx      min    4591.4 ms  median    4737.6 ms  alloc    75.7 MiB
t60k.xlsx              min     785.4 ms  median     792.4 ms  alloc    21.7 MiB
== cand
stack1600.xlsx         min     271.3 ms  median     319.1 ms  alloc    11.8 MiB
stack3200.xlsx         min     517.8 ms  median     525.5 ms  alloc    23.3 MiB
sparse6250.xlsx        min     320.3 ms  median     339.8 ms  alloc    16.3 MiB
sparse12500.xlsx       min     733.2 ms  median     757.3 ms  alloc    32.7 MiB
mixed_sparse.xlsx      min    1686.5 ms  median    1805.8 ms  alloc    75.9 MiB
t60k.xlsx              min     808.8 ms  median     809.6 ms  alloc    21.7 MiB
== base
stack1600.xlsx         min     255.1 ms  median     273.3 ms  alloc    11.6 MiB
stack3200.xlsx         min     523.1 ms  median     556.3 ms  alloc    23.0 MiB
sparse6250.xlsx        min     538.1 ms  median     541.3 ms  alloc    16.2 MiB
sparse12500.xlsx       min    1545.6 ms  median    1604.5 ms  alloc    32.6 MiB
mixed_sparse.xlsx      min    4525.4 ms  median    4684.6 ms  alloc    75.7 MiB
t60k.xlsx              min     805.9 ms  median     837.9 ms  alloc    21.7 MiB
== cand
stack1600.xlsx         min     256.7 ms  median     278.2 ms  alloc    11.8 MiB
stack3200.xlsx         min     564.4 ms  median     600.8 ms  alloc    23.3 MiB
sparse6250.xlsx        min     312.4 ms  median     345.6 ms  alloc    16.3 MiB
sparse12500.xlsx       min     722.3 ms  median     780.7 ms  alloc    32.7 MiB
mixed_sparse.xlsx      min    1712.8 ms  median    1728.0 ms  alloc    75.9 MiB
t60k.xlsx              min     771.6 ms  median     786.9 ms  alloc    21.7 MiB
```
