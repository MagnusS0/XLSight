# E15 retention probe: GC.GetTotalMemory(true) delta, workbook reference held

Probe: wave-2 Memory worker `probe.cs`, run by the lead against base 73a1400 and the candidate tree.

## base
```
strings_unique.xlsx    fullscan  result=160004 retained(open)=17783 KiB retained(disposed, ref held)=17776 KiB
strings_unique.xlsx    analyze   result=1 retained(open)=6257 KiB retained(disposed, ref held)=6252 KiB
string_heavy.xlsb      fullscan  result=10005 retained(open)=39 KiB retained(disposed, ref held)=39 KiB
string_heavy.xlsb      analyze   result=1 retained(open)=80 KiB retained(disposed, ref held)=73 KiB
large.xlsb             fullscan  result=500005 retained(open)=13 KiB retained(disposed, ref held)=13 KiB
large.xlsb             analyze   result=1 retained(open)=30 KiB retained(disposed, ref held)=22 KiB
complex_workbook.xlsb  fullscan  result=381 retained(open)=25 KiB retained(disposed, ref held)=25 KiB
complex_workbook.xlsb  analyze   result=4 retained(open)=258 KiB retained(disposed, ref held)=186 KiB
```

## cand
```
strings_unique.xlsx    fullscan  result=160004 retained(open)=17783 KiB retained(disposed, ref held)=7 KiB
strings_unique.xlsx    analyze   result=1 retained(open)=6265 KiB retained(disposed, ref held)=16 KiB
string_heavy.xlsb      fullscan  result=10005 retained(open)=39 KiB retained(disposed, ref held)=9 KiB
string_heavy.xlsb      analyze   result=1 retained(open)=80 KiB retained(disposed, ref held)=42 KiB
large.xlsb             fullscan  result=500005 retained(open)=13 KiB retained(disposed, ref held)=9 KiB
large.xlsb             analyze   result=1 retained(open)=22 KiB retained(disposed, ref held)=9 KiB
complex_workbook.xlsb  fullscan  result=381 retained(open)=25 KiB retained(disposed, ref held)=19 KiB
complex_workbook.xlsb  analyze   result=4 retained(open)=193 KiB retained(disposed, ref held)=127 KiB
```
