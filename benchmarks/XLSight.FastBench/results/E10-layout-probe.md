# E10 layout probe: base = HEAD 39a10a8, cand = + query-candidate-4; alternating processes, rebuilt with --no-cache (an earlier attempt measured stale cached binaries and was discarded)
```
== round 1 base
stack1600.xlsx         min     292.7 ms  median     349.1 ms  alloc    11.5 MiB
stack3200.xlsx         min     178.3 ms  median     181.1 ms  alloc    22.9 MiB
sparse6250.xlsx        min    1079.6 ms  median    1122.0 ms  alloc    16.4 MiB
sparse12500.xlsx       min    5058.5 ms  median    5076.9 ms  alloc    33.0 MiB
mixed_sparse.xlsx      min   12877.4 ms  median   12879.7 ms  alloc    72.2 MiB
t60k.xlsx              min     225.7 ms  median     250.2 ms  alloc    17.6 MiB
== round 1 cand
stack1600.xlsx         min     155.6 ms  median     176.1 ms  alloc    11.5 MiB
stack3200.xlsx         min     166.7 ms  median     173.5 ms  alloc    22.8 MiB
sparse6250.xlsx        min     187.5 ms  median     194.2 ms  alloc    16.1 MiB
sparse12500.xlsx       min     371.4 ms  median     407.2 ms  alloc    32.3 MiB
mixed_sparse.xlsx      min     985.6 ms  median    1021.7 ms  alloc    71.1 MiB
t60k.xlsx              min     212.0 ms  median     245.8 ms  alloc    17.6 MiB
== round 2 base
stack1600.xlsx         min     125.2 ms  median     289.9 ms  alloc    11.5 MiB
stack3200.xlsx         min     198.9 ms  median     199.0 ms  alloc    22.9 MiB
sparse6250.xlsx        min    1051.5 ms  median    1111.0 ms  alloc    16.4 MiB
sparse12500.xlsx       min    5198.6 ms  median    5318.2 ms  alloc    33.0 MiB
mixed_sparse.xlsx      min   12986.6 ms  median   13037.7 ms  alloc    72.2 MiB
t60k.xlsx              min     253.0 ms  median     263.6 ms  alloc    17.6 MiB
== round 2 cand
stack1600.xlsx         min     176.2 ms  median     230.9 ms  alloc    11.5 MiB
stack3200.xlsx         min     173.1 ms  median     179.5 ms  alloc    22.8 MiB
sparse6250.xlsx        min     161.4 ms  median     220.0 ms  alloc    16.1 MiB
sparse12500.xlsx       min     357.2 ms  median     357.7 ms  alloc    32.3 MiB
mixed_sparse.xlsx      min     911.4 ms  median     930.7 ms  alloc    71.1 MiB
t60k.xlsx              min     247.3 ms  median     255.9 ms  alloc    17.6 MiB
```
