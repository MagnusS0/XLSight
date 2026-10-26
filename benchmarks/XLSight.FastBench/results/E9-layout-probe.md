# E9 layout probe: AnalyzeLayout, 1 warm-up + 3 timed calls per file, base (HEAD 172e37b) vs cand (+ query-candidate-3), alternating processes
```csharp
#:project PROJECT
#:property TreatWarningsAsErrors=false
using System.Diagnostics;
using XLSight;
using XLSight.Layout;
string[] files = ["/tmp/claude-0/-home-user-XLSight/90e52a37-8d24-5ab7-ac09-9fb4973a882b/scratchpad/w-query/data/stack1600.xlsx", "/tmp/claude-0/-home-user-XLSight/90e52a37-8d24-5ab7-ac09-9fb4973a882b/scratchpad/w-query/data/stack3200.xlsx", "/tmp/claude-0/-home-user-XLSight/90e52a37-8d24-5ab7-ac09-9fb4973a882b/scratchpad/w-query/data/sparse6250.xlsx", "/tmp/claude-0/-home-user-XLSight/90e52a37-8d24-5ab7-ac09-9fb4973a882b/scratchpad/w-query/data/sparse12500.xlsx", "/home/user/XLSight/artifacts/fastbench/fixtures/mixed_sparse.xlsx", "/tmp/claude-0/-home-user-XLSight/90e52a37-8d24-5ab7-ac09-9fb4973a882b/scratchpad/w-query/data/t60k.xlsx"];
foreach (string f in files)
{
    using var wb = ExcelWorkbook.Open(f);
    string sheet = wb.SheetNames[0];
    wb.AnalyzeLayout(sheet); // warm
    var times = new List<double>();
    long alloc0 = GC.GetTotalAllocatedBytes(true);
    for (int i = 0; i < 3; i++) { var sw = Stopwatch.StartNew(); var l = wb.AnalyzeLayout(sheet); times.Add(sw.Elapsed.TotalMilliseconds); GC.KeepAlive(l); }
    long alloc = (GC.GetTotalAllocatedBytes(true) - alloc0) / 3;
    times.Sort();
    Console.WriteLine($"{Path.GetFileName(f),-22} min {times[0],9:F1} ms  median {times[1],9:F1} ms  alloc {alloc / 1048576.0,7:F1} MiB");
}
```
```
== round 1 base
stack1600.xlsx         min     220.2 ms  median     339.6 ms  alloc    11.6 MiB
stack3200.xlsx         min     457.3 ms  median     462.8 ms  alloc    23.0 MiB
sparse6250.xlsx        min    1065.2 ms  median    1084.4 ms  alloc    16.5 MiB
sparse12500.xlsx       min    5066.7 ms  median    5239.1 ms  alloc    33.3 MiB
mixed_sparse.xlsx      min   13017.7 ms  median   13402.1 ms  alloc    72.5 MiB
t60k.xlsx              min     254.4 ms  median     293.4 ms  alloc    17.6 MiB
== round 1 cand
stack1600.xlsx         min     113.4 ms  median     244.9 ms  alloc    11.6 MiB
stack3200.xlsx         min     185.3 ms  median     231.0 ms  alloc    22.9 MiB
sparse6250.xlsx        min    1023.2 ms  median    1054.0 ms  alloc    16.4 MiB
sparse12500.xlsx       min    5064.6 ms  median    5214.6 ms  alloc    33.0 MiB
mixed_sparse.xlsx      min   12293.8 ms  median   12711.3 ms  alloc    72.2 MiB
t60k.xlsx              min     237.5 ms  median     252.7 ms  alloc    17.6 MiB
== round 2 base
stack1600.xlsx         min     154.8 ms  median     351.6 ms  alloc    11.5 MiB
stack3200.xlsx         min     495.2 ms  median     498.0 ms  alloc    23.0 MiB
sparse6250.xlsx        min    1060.7 ms  median    1146.2 ms  alloc    16.5 MiB
sparse12500.xlsx       min    5242.4 ms  median    5273.3 ms  alloc    33.3 MiB
mixed_sparse.xlsx      min   13167.5 ms  median   13359.1 ms  alloc    72.5 MiB
t60k.xlsx              min     229.9 ms  median     237.4 ms  alloc    17.6 MiB
== round 2 cand
stack1600.xlsx         min     274.0 ms  median     289.0 ms  alloc    11.5 MiB
stack3200.xlsx         min     200.8 ms  median     221.9 ms  alloc    22.9 MiB
sparse6250.xlsx        min    1071.4 ms  median    1083.1 ms  alloc    16.4 MiB
sparse12500.xlsx       min    5285.3 ms  median    5376.3 ms  alloc    33.0 MiB
mixed_sparse.xlsx      min   12949.1 ms  median   12964.9 ms  alloc    72.2 MiB
t60k.xlsx              min     254.9 ms  median     272.0 ms  alloc    17.6 MiB
```
