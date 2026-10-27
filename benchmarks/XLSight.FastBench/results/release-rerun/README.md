# Release re-runs of the file-based probes

The lead's probes are .NET file-based apps (`dotnet run probe.cs`) that reference the library with
`#:project`. The directive `#:property Configuration=Release` does not make that referenced project
build in Release: plain `dotnet run probe.cs` linked the **Debug** `XLSight.dll` (its hash matched
`src/XLSight/bin/Debug`). Only `dotnet run -c Release probe.cs` builds the library optimized.

Every probe table recorded with plain `dotnet run` therefore measured unoptimized library code. Each
file here re-runs one probe with `-c Release` (`--no-cache` on the first round), at the same base
and candidate revisions.
- `rerun.sh` drives E3 and E9–E12.
- E15–E19 were re-run with their original base/cand trees; their cand is today's head.

FastBench itself (`fastbench.sh build`, `ab`, `confirm`) always built Release, so every FastBench
verdict in `EXPERIMENTS.md` is unaffected.
