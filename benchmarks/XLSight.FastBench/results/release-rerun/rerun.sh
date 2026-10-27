#!/usr/bin/env bash
# rerun.sh NAME BASE_REV CAND (rev or patch path) TEMPLATE PROJECT_SUBPATH ROUNDS
set -u
name=$1 base=$2 cand=$3 tmpl=$4 proj=$5 rounds=${6:-2}
R=$(dirname "$0"); W=$R/wt-$name; mkdir -p $W
cd /home/user/XLSight
for side in base cand; do
  rm -rf $W/$side-tree; git worktree prune
  if [[ $side == base ]]; then git worktree add -q --detach $W/$side-tree $base
  elif [[ -f $cand ]]; then git worktree add -q --detach $W/$side-tree $base && git -C $W/$side-tree apply $cand || { echo "patch failed"; exit 1; }
  else git worktree add -q --detach $W/$side-tree $cand; fi
  mkdir -p $W/$side-probe
  sed "s#PROJECT#$W/$side-tree/$proj#" $tmpl > $W/$side-probe/probe.cs
done
export CI=true
for i in $(seq 1 $rounds); do for side in base cand; do
  echo "== round $i $side"
  (cd $W/$side-probe && dotnet run -c Release $([ $i = 1 ] && echo --no-cache) probe.cs 2>&1 | grep -v 'warning\|^$')
done; done
for side in base cand; do git worktree remove --force $W/$side-tree; done
