# Agent Instructions

## Toolchain
- .NET SDK pinned in `global.json`; target `net10.0`, C# 14.
- Tests use xUnit v3 on Microsoft.Testing.Platform.
- Package versions are central in `Directory.Packages.props`; do not put versions in `.csproj` files.
- Local-only notes, reviews, and drafts go in `.local/` (gitignored).
- Python helper scripts: run with `uv run` or `uvx --with <pkg>`, not bare `python3`.

## Commands
| Task | Command |
|------|---------|
| Build | `dotnet build XLSight.slnx` |
| Test one project | `dotnet test --project tests/XLSight.Tests/XLSight.Tests.csproj` |
| Test one class | `dotnet test --project tests/XLSight.Layout.Tests/XLSight.Layout.Tests.csproj --filter-class "*LayoutInferenceSyntheticTests"` |
| Test one method | add `--filter-method "*MethodName"` |
| Full CI test | `dotnet test --solution XLSight.slnx -c Release --max-parallel-test-modules 1` |
| Benchmarks | `dotnet run -c Release --project benchmarks/XLSight.Benchmarks -- --filter "*Name*"` |

- Always pass `--project`. `dotnet test path/to.csproj` exits 0 without running any tests.
- Treat a run as passing only when the `Test run summary` shows a non-zero `total`.

## Key Conventions
- Warnings are errors in `src/`; fix analyzer warnings instead of suppressing them.
- Local builds rewrite `**/packages.lock.json` (SourceLink entries are CI-only). Revert those changes with `git checkout -- '**/packages.lock.json'`; never commit them.
- Streaming first: no DOM loading and no full materialization on default read paths.
- Peak RSS matters more than BenchmarkDotNet "Allocated"; check both for performance changes.
- Name tests after the behavior under test, not after work phases.
- Public API changes must update the matching page in `docs/` or the package README.
- `dotnet run file.cs` caches stale builds of referenced projects; clear `~/.local/share/dotnet/runfile/` if output contradicts tests.

## External References
| Need | File |
|------|------|
| Public API usage | `docs/reading.md`, `docs/analysis.md`, `docs/concurrency.md` |
| Benchmarks and methodology | `docs/performance.md` |
| Layout package | `src/XLSight.Layout/README.md` |
| Query package | `src/XLSight.Query/README.md` |
| Code style | `.editorconfig` |
| CI pipeline | `.github/workflows/ci.yml` |
| Release / NuGet publish | `.github/workflows/pack.yml` (runs on `v*` tags) |
| Documentation standards | `.agents/skills/dotnet-documentation/SKILL.md` |

## Git
- Commits: Conventional Commits `type(scope): subject`; body at most ~70 words.
- Branch from `master`; open PRs as drafts.
