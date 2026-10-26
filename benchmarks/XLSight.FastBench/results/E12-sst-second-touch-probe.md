# E12 probe: second-touch SST caching (base = 34b6b43, cand = + memory-candidate-3), two alternating rounds
```csharp
#:project PROJECT
#:property TreatWarningsAsErrors=false
using System.Diagnostics;
using XLSight;
string f = "/home/user/XLSight/artifacts/fastbench/fixtures/strings_unique.xlsx";
string r = "/home/user/XLSight/artifacts/fastbench/fixtures/strings_repeated.xlsx";
long hwm0 = Hwm();
using (var wb = ExcelWorkbook.Open(f))
{
    var t = Stopwatch.StartNew(); long a0 = GC.GetTotalAllocatedBytes(true); Scan(wb, "Data");
    Console.WriteLine($"unique scan1   {t.Elapsed.TotalMilliseconds,7:F1} ms alloc {(GC.GetTotalAllocatedBytes(true) - a0) / 1048576.0,6:F2} MiB retained {GC.GetTotalMemory(true) / 1048576.0,6:F2} MiB peakRSS {Hwm() / 1048576.0,6:F1} MiB (start {hwm0 / 1048576.0:F1})");
    t.Restart(); a0 = GC.GetTotalAllocatedBytes(true); Scan(wb, "Data");
    Console.WriteLine($"unique scan2   {t.Elapsed.TotalMilliseconds,7:F1} ms alloc {(GC.GetTotalAllocatedBytes(true) - a0) / 1048576.0,6:F2} MiB retained {GC.GetTotalMemory(true) / 1048576.0,6:F2} MiB");
}
using (var wb = ExcelWorkbook.Open(r))
{
    long a0 = GC.GetTotalAllocatedBytes(true); Scan(wb, "Data");
    Console.WriteLine($"repeated scan1 alloc {(GC.GetTotalAllocatedBytes(true) - a0) / 1048576.0,6:F2} MiB");
    a0 = GC.GetTotalAllocatedBytes(true); Scan(wb, "Data");
    Console.WriteLine($"repeated scan2 alloc {(GC.GetTotalAllocatedBytes(true) - a0) / 1048576.0,6:F2} MiB");
}
static void Scan(ExcelWorkbook wb, string s) { long n = 0; using var rd = wb.GetSheetReader(s); while (rd.Read()) foreach (var c in rd.Current.Cells) if (c.TryGetText(out var t)) n += t.Length; GC.KeepAlive(n); }
static long Hwm() { foreach (var l in File.ReadLines("/proc/self/status")) if (l.StartsWith("VmHWM:")) return long.Parse(l[6..].Trim().Split(' ')[0]) * 1024; return 0; }
```
```
== 1 base
unique scan1     649.8 ms alloc  20.05 MiB retained  17.83 MiB peakRSS   61.0 MiB (start 23.8)
unique scan2     277.6 ms alloc   2.28 MiB retained  17.83 MiB
repeated scan1 alloc   0.25 MiB
repeated scan2 alloc   0.00 MiB
== 1 cand
unique scan1     650.5 ms alloc  20.05 MiB retained   7.54 MiB peakRSS   68.1 MiB (start 23.8)
unique scan2     274.3 ms alloc  12.56 MiB retained  17.83 MiB
repeated scan1 alloc   0.38 MiB
repeated scan2 alloc   0.00 MiB
== 2 base
unique scan1     652.9 ms alloc  20.05 MiB retained  17.83 MiB peakRSS   60.9 MiB (start 23.7)
unique scan2     241.6 ms alloc   2.28 MiB retained  17.83 MiB
repeated scan1 alloc   0.25 MiB
repeated scan2 alloc   0.00 MiB
== 2 cand
unique scan1     646.6 ms alloc  20.05 MiB retained   7.54 MiB peakRSS   68.1 MiB (start 23.8)
unique scan2     241.0 ms alloc  12.56 MiB retained  17.83 MiB
repeated scan1 alloc   0.38 MiB
repeated scan2 alloc   0.00 MiB
```
