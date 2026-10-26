#!/usr/bin/env bash
# XLSight fast benchmark driver. See README.md in this directory.
#
#   fastbench.sh restore            dotnet restore (locked) for the runner and libraries
#   fastbench.sh build              Release build of the runner (and the libraries it references)
#   fastbench.sh fixtures [--force] [--confirm]  generate deterministic synthetic fixtures
#   fastbench.sh prepare            restore + build + fixtures, each timed
#   fastbench.sh run [runner args]  the capped fast suite (never builds, never generates)
#   fastbench.sh confirm [REGEX]    focused longer confirmation tier (large fixtures, ≤200 s cap)
#   fastbench.sh compare --base A.json... --cand B.json...
#   fastbench.sh ab <base-ref> [rounds]   alternate baseline/candidate runs, then compare
#   fastbench.sh selftest           demonstrate deadline, watchdog and missing-fixture handling
set -uo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
PROJECT="$HERE/XLSight.FastBench.csproj"
BIN_DIR="${FASTBENCH_BIN:-$HERE/bin/Release/net10.0}"
DLL="$BIN_DIR/XLSight.FastBench.dll"
OUT_DIR="${FASTBENCH_OUT:-$ROOT/artifacts/fastbench}"

export CI=true DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

now_ms() { date +%s%3N; }

timed() {
  local label="$1"; shift
  local t0; t0=$(now_ms)
  "$@"; local rc=$?
  echo "[$label] $(( $(now_ms) - t0 )) ms (exit $rc)" >&2
  return $rc
}

# The fast command refuses to measure a binary older than any source it was built from.
check_fresh() {
  if [[ ! -f "$DLL" ]]; then
    echo "error: $DLL not built; run '$0 build' first." >&2
    return 1
  fi
  if [[ -n "${FASTBENCH_BIN:-}" ]]; then
    return 0  # an explicitly selected build (e.g. an A/B baseline) is checked by whoever built it
  fi
  local newer
  newer=$(find "$ROOT/src" "$HERE" -newer "$DLL" -type f \( -name '*.cs' -o -name '*.csproj' -o -name '*.props' -o -name '*.targets' \) \
    -not -path '*/bin/*' -not -path '*/obj/*' -print -quit 2>/dev/null)
  for f in "$ROOT/Directory.Build.props" "$ROOT/Directory.Build.targets" "$ROOT/Directory.Packages.props"; do
    [[ -z "$newer" && "$f" -nt "$DLL" ]] && newer="$f"
  done
  if [[ -n "$newer" ]]; then
    echo "error: stale build: $newer is newer than $DLL; run '$0 build' first." >&2
    return 1
  fi
}

git_identity() {
  if [[ -n "${FASTBENCH_GIT_REV:-}" ]]; then GIT_REV="$FASTBENCH_GIT_REV"; GIT_DIRTY="${FASTBENCH_GIT_DIRTY:-unknown}"; return; fi
  GIT_REV=$(git -C "$ROOT" rev-parse --short=12 HEAD 2>/dev/null || echo unknown)
  if git -C "$ROOT" diff --quiet HEAD -- src 2>/dev/null; then
    GIT_DIRTY=clean
  else
    GIT_DIRTY="src-diff-$(git -C "$ROOT" diff HEAD -- src | sha256sum | cut -c1-12)"
  fi
}

# Runs the suite under an external watchdog. `timeout` puts the runner in its own process group
# and signals the whole group, so isolated child processes die with it.
run_suite() {
  local WALL_CAP_S="${FASTBENCH_WALL_CAP_S:-30}"
  check_fresh || return 1
  git_identity
  mkdir -p "$OUT_DIR"
  local t0 rc t1
  t0=$(now_ms)
  timeout --kill-after=2s "${WALL_CAP_S}s" dotnet "$DLL" run --root "$ROOT" --out "$OUT_DIR" \
    --git-rev "$GIT_REV" --git-dirty "$GIT_DIRTY" "$@"
  rc=$?
  t1=$(now_ms)
  local wall=$(( t1 - t0 ))
  if [[ $rc -eq 124 || $rc -eq 137 ]]; then
    local stub
    stub="$OUT_DIR/$(date -u +%Y%m%d-%H%M%S)-watchdog-killed.json"
    printf '{"schema":"xlsight-fastbench/1","status":"failed","exitCode":%d,"problems":["external watchdog killed the runner after %ss"],"git":{"revision":"%s","dirty":"%s"},"timing":{"wallMs":%d}}\n' \
      "$rc" "$WALL_CAP_S" "$GIT_REV" "$GIT_DIRTY" "$wall" > "$stub"
    echo "FAILED: external watchdog killed the benchmark after ${WALL_CAP_S}s (exit $rc); wrote $stub" >&2
    echo "wall-clock (process start to exit, as seen by the driver): ${wall} ms" >&2
    return 5
  fi
  echo "wall-clock (process start to exit, as seen by the driver): ${wall} ms; cap ${WALL_CAP_S}000 ms" >&2
  if [[ $rc -eq 0 && $wall -gt $(( WALL_CAP_S * 1000 )) ]]; then
    echo "FAILED: wall-clock ${wall} ms exceeded the ${WALL_CAP_S}s cap" >&2
    return 5
  fi
  return $rc
}

cmd="${1:-run}"; shift || true
case "$cmd" in
  restore)
    timed restore dotnet restore "$PROJECT" --locked-mode -m:1 ;;
  build)
    timed build dotnet build "$PROJECT" -c Release --no-restore -m:1 -nologo -clp:ErrorsOnly ;;
  fixtures)
    check_fresh || exit 1
    timed fixtures dotnet "$DLL" fixtures --root "$ROOT" "$@" ;;
  prepare)
    "$0" restore && "$0" build && "$0" fixtures ;;
  run)
    run_suite "$@" ;;
  confirm)
    filter="${1:-^confirm\.}"; shift || true
    FASTBENCH_WALL_CAP_S=200 run_suite --filter "$filter" --budget-ms 180000 --label "${FASTBENCH_LABEL:-confirm}" "$@" ;;
  compare)
    check_fresh || exit 1
    dotnet "$DLL" compare "$@" ;;
  ab)
    base_ref="${1:?usage: ab <base-ref> [rounds]}"; rounds="${2:-3}"
    base_tree="$ROOT/artifacts/fastbench/ab-base"
    rm -rf "$base_tree"; git -C "$ROOT" worktree prune
    git -C "$ROOT" worktree add --detach "$base_tree" "$base_ref" >/dev/null || exit 1
    # Same runner and manifest on both sides; only the library sources differ.
    rm -rf "$base_tree/benchmarks/XLSight.FastBench"
    mkdir -p "$base_tree/benchmarks/XLSight.FastBench"
    (cd "$HERE" && tar --exclude=bin --exclude=obj -cf - .) | tar -C "$base_tree/benchmarks/XLSight.FastBench" -xf -
    timed build-base dotnet build "$base_tree/benchmarks/XLSight.FastBench/XLSight.FastBench.csproj" -c Release -m:1 -nologo -clp:ErrorsOnly || exit 1
    "$0" build || exit 1
    base_files=(); cand_files=()
    for i in $(seq 1 "$rounds"); do
      for side in base cand; do
        if [[ $side == base ]]; then
          bin="$base_tree/benchmarks/XLSight.FastBench/bin/Release/net10.0"
          rev="$(git -C "$ROOT" rev-parse --short=12 "$base_ref")"; dirty="clean-base-worktree"
        else
          bin="$BIN_DIR"; rev=""; dirty=""
        fi
        FASTBENCH_BIN="$bin" FASTBENCH_GIT_REV="$rev" FASTBENCH_GIT_DIRTY="$dirty" "$0" run --label "ab-$side-$i" "$@" >/dev/null 2>&1
        f=$(ls -t "$OUT_DIR"/*-ab-$side-$i.json | head -1)
        if [[ $side == base ]]; then base_files+=("$f"); else cand_files+=("$f"); fi
        echo "round $i $side: $(grep -m1 -o '"status": "[a-z]*"' "$f")" >&2
      done
    done
    dotnet "$DLL" compare --base "${base_files[@]}" --cand "${cand_files[@]}"
    git -C "$ROOT" worktree remove --force "$base_tree" ;;
  selftest)
    rc_all=0
    echo "== 1. suite budget too small for the default suite: expect status incomplete, exit 3" >&2
    run_suite --budget-ms 3000 --label selftest-budget >/dev/null; rc=$?
    echo "   exit $rc" >&2; [[ $rc -eq 3 ]] || rc_all=1
    echo "== 2. over-budget probe case (100 × 400 ms samples): expect incomplete, exit 3, under the cap" >&2
    run_suite --filter '^probe\.over_budget$' --budget-ms 5000 --label selftest-probe >/dev/null; rc=$?
    echo "   exit $rc" >&2; [[ $rc -eq 3 ]] || rc_all=1
    echo "== 3. hung op, internal hard deadline: expect exit 4 with an incomplete result file" >&2
    run_suite --filter '^probe\.hang$' --budget-ms 3000 --label selftest-hard >/dev/null; rc=$?
    echo "   exit $rc" >&2; [[ $rc -eq 4 ]] || rc_all=1
    echo "== 4. hung op, internal watchdog disabled: expect the external watchdog to kill it (exit 5)" >&2
    FASTBENCH_WALL_CAP_S=6 run_suite --filter '^probe\.hang$' --no-internal-watchdog --label selftest-ext >/dev/null; rc=$?
    echo "   exit $rc" >&2; [[ $rc -eq 5 ]] || rc_all=1
    sleep 1
    if pgrep -f '^dotnet .*XLSight.FastBench.dll run' >/dev/null; then echo "   leftover runner processes after watchdog kill" >&2; rc_all=1; else echo "   no runner processes left behind" >&2; fi
    echo "== 5. missing fixture: expect failed, exit 2" >&2
    tmp_manifest="$OUT_DIR/selftest-manifest.json"
    sed 's#tests/XLSight.Tests/TestData/small.xlsx#tests/XLSight.Tests/TestData/does-not-exist.xlsx#' "$HERE/manifest.json" > "$tmp_manifest"
    run_suite --manifest "$tmp_manifest" --filter '^open\.small$' --label selftest-missing >/dev/null; rc=$?
    echo "   exit $rc" >&2; [[ $rc -eq 2 ]] || rc_all=1
    echo "== 6. stale binary: expect refusal, exit 1" >&2
    touch -d '+1 minute' "$HERE/Digest.cs"; run_suite --filter '^open\.small$' >/dev/null 2>&1; rc=$?
    touch -r "$DLL" -d '-1 minute' "$HERE/Digest.cs"
    echo "   exit $rc" >&2; [[ $rc -eq 1 ]] || rc_all=1
    [[ $rc_all -eq 0 ]] && echo "selftest: all deadline/failure paths behaved as expected" >&2 || echo "selftest: FAILURES above" >&2
    exit $rc_all ;;
  *)
    echo "unknown command '$cmd'" >&2; exit 1 ;;
esac
