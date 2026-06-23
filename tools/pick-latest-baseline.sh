#!/usr/bin/env bash
#
# pick-latest-baseline.sh — select the most recent date-stamped benchmark
# baseline matching a glob, or fail loudly when none exist.
#
# Restored-audit fix (#47 / old #850). The nightly drift workflows previously
# resolved a baseline with:
#
#   BASELINE=$(ls -1 <glob> 2>/dev/null | ... | head -1)
#   if [ -z "$BASELINE" ]; then echo "No baseline; skipping diff."; exit 0; fi
#
# which (a) goes GREEN when nothing matches — silently disabling drift
# detection after a baseline rename / deletion / path typo — and (b) can abort
# under `set -eo pipefail` before the empty check is even reached, because the
# no-match `ls` exits 2. This helper makes "no baseline" a hard, noisy failure
# and is robust to pipefail.
#
# Usage:
#   if ! BASELINE=$(tools/pick-latest-baseline.sh "<glob>"); then
#     echo "::error::..."; exit 1
#   fi
#
# On success: prints the chosen baseline path to stdout and exits 0.
# On no match: prints a diagnostic to stderr and exits 1 (the caller is
# expected to emit the GitHub `::error::` annotation so it reaches stdout).
#
# Reproduce locally:
#   # missing-baseline path (exit 1):
#   tools/pick-latest-baseline.sh "/tmp/nope/foo-*.jsonl"; echo "exit=$?"
#   # happy path (prints the newest date):
#   mkdir -p /tmp/bl && touch /tmp/bl/bench-sa-merlin-2026-05-2{4,5}.jsonl
#   tools/pick-latest-baseline.sh "/tmp/bl/bench-sa-merlin-*.jsonl"
#
set -euo pipefail

if [ "$#" -lt 1 ] || [ -z "${1:-}" ]; then
  echo "pick-latest-baseline.sh: usage: pick-latest-baseline.sh <glob>" >&2
  exit 2
fi

pattern="$1"

shopt -s nullglob
# Intentional glob expansion of $pattern; baseline paths contain no spaces.
# shellcheck disable=SC2206
files=( $pattern )
shopt -u nullglob

if [ "${#files[@]}" -eq 0 ]; then
  echo "pick-latest-baseline.sh: no baseline matched glob: $pattern" >&2
  exit 1
fi

# Newest by the trailing YYYY-MM-DD date. Preserve the historical sort idiom
# (the -@ sentinel keeps the date field sorting independently of any longer
# sibling stem), but read the result into an array so nothing closes the pipe
# early under `set -o pipefail` (no `head`).
mapfile -t sorted < <(printf '%s\n' "${files[@]}" \
  | sed 's/\([0-9]\{4\}-[0-9]\{2\}-[0-9]\{2\}\)\.jsonl$/\1-@.jsonl/' \
  | LC_ALL=C sort -r \
  | sed 's/-@\.jsonl$/.jsonl/')

printf '%s\n' "${sorted[0]}"
