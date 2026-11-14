#!/usr/bin/env bash
# Builds the synthetic SQL repo for the second sandbox adapter as a bare git repo.
# main = examples/sql-repo/base; each examples/sql-repo/changes/<branch>/ is overlaid on main as
# one commit on branch <branch>. Author and dates are fixed, so the shas are reproducible.
# Usage: examples/make_sql_repo.sh [target]   (default: work/sql-repo.git, gitignored)
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/.." && pwd)"
src="$here/sql-repo"
target="${1:-$root/work/sql-repo.git}"

export GIT_AUTHOR_NAME="Receipts Fixture" GIT_AUTHOR_EMAIL="fixture@receipts.local"
export GIT_COMMITTER_NAME="$GIT_AUTHOR_NAME" GIT_COMMITTER_EMAIL="$GIT_AUTHOR_EMAIL"
export GIT_AUTHOR_DATE="2026-11-14T09:00:00Z" GIT_COMMITTER_DATE="2026-11-14T09:00:00Z"

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

git init -q -b main "$tmp/repo"
cp -R "$src/base/." "$tmp/repo/"
git -C "$tmp/repo" add -A
git -C "$tmp/repo" commit -q -m "Create orders table and revenue report"

for dir in "$src"/changes/*/; do
  branch="$(basename "$dir")"
  git -C "$tmp/repo" checkout -q -b "$branch" main
  cp -R "$dir." "$tmp/repo/"
  git -C "$tmp/repo" add -A
  git -C "$tmp/repo" commit -q -m "$branch"
  git -C "$tmp/repo" checkout -q main
done

rm -rf "$target"
mkdir -p "$(dirname "$target")"
git clone -q --bare "$tmp/repo" "$target"
git -C "$target" for-each-ref --format='%(refname:short) %(objectname:short)' refs/heads
