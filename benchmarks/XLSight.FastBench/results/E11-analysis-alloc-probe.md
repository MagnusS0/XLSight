# E11 analysis allocation probe (second Analyze per file, maxDegreeOfParallelism 1; base = 27fc006, cand = + query-candidate-2)
```csharp
#:project PROJECT
#:property TreatWarningsAsErrors=false
using XLSight;
using XLSight.Analysis;
foreach (string f in new[] { "/home/user/XLSight/artifacts/fastbench/fixtures/strings_unique.xlsx", "/home/user/XLSight/artifacts/fastbench/fixtures/strings_repeated.xlsx", "/home/user/XLSight/tests/XLSight.Layout.Tests/TestData/03_Banking_golden.xlsx" })
foreach (var level in new[] { AnalysisLevel.Observed, AnalysisLevel.Full })
{
    using (var w = ExcelWorkbook.Open(f)) { w.Analyze(level, maxDegreeOfParallelism: 1); }
    long a0 = GC.GetTotalAllocatedBytes(true);
    using (var w = ExcelWorkbook.Open(f)) { w.Analyze(level, maxDegreeOfParallelism: 1); }
    Console.WriteLine($"{Path.GetFileName(f),-28} {level,-9} alloc {(GC.GetTotalAllocatedBytes(true) - a0) / 1048576.0,7:F2} MiB");
}
```
```
== base
strings_unique.xlsx          Observed  alloc   19.94 MiB
strings_unique.xlsx          Full      alloc   19.94 MiB
strings_repeated.xlsx        Observed  alloc    0.58 MiB
strings_repeated.xlsx        Full      alloc    0.58 MiB
03_Banking_golden.xlsx       Observed  alloc    3.55 MiB
03_Banking_golden.xlsx       Full      alloc    3.63 MiB
== cand
strings_unique.xlsx          Observed  alloc    6.42 MiB
strings_unique.xlsx          Full      alloc    6.41 MiB
strings_repeated.xlsx        Observed  alloc    0.46 MiB
strings_repeated.xlsx        Full      alloc    0.46 MiB
03_Banking_golden.xlsx       Observed  alloc    3.55 MiB
03_Banking_golden.xlsx       Full      alloc    3.65 MiB
```
