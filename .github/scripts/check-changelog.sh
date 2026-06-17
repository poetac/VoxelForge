#!/usr/bin/env bash
# CHANGELOG.md update check (2026-05-17): surface PRs that touch
# production code under Voxelforge*/ without also updating the
# SSOT-for-what-shipped log. Non-blocking on this free-tier private
# repo (no branch protection per audit-prep C1) — the workflow turns
# the script's verdict into a sticky PR comment.
#
# Usage:
#   .github/scripts/check-changelog.sh [base-ref]
# Default base-ref is `origin/main`.
#
# Exit codes:
#   0 — CHANGELOG.md updated OR no production-code change detected
#   2 — production code changed but CHANGELOG.md did not
#
# Production code = anything under `Voxelforge*/` excluding:
#   • test projects                 (`Voxelforge*.Tests/`)
#   • per-project docs surface      (`Voxelforge*/docs/`)
#   • markdown anywhere in the tree (`*.md`)
# Tooling and infrastructure paths (`.github/`, `tools/`, `assets/`,
# `site/`) do not trigger the check; apply the `skip-changelog` label
# for the rare revert / hotfix that needs an explicit exemption.
set -euo pipefail

BASE="${1:-origin/main}"
CHANGELOG="CHANGELOG.md"

CHANGED=$(git diff --name-only "$BASE" --)

if echo "$CHANGED" | grep -qx "$CHANGELOG"; then
  echo "OK: $CHANGELOG was updated."
  exit 0
fi

PROD=$(echo "$CHANGED" \
  | grep -E '^Voxelforge[^/]*/' \
  | grep -vE '^Voxelforge[^/]*\.Tests/' \
  | grep -vE '^Voxelforge[^/]*/docs/' \
  | grep -vE '\.md$' \
  || true)

if [[ -z "${PROD//[[:space:]]/}" ]]; then
  echo "OK: no production-code change detected vs $BASE."
  exit 0
fi

cat <<EOF
MISS: production code changed vs $BASE but $CHANGELOG was not updated.

Production files in this PR:
EOF
echo "$PROD" | sed 's/^/  /'
cat <<EOF

Add a CHANGELOG.md entry describing the change (Keep a Changelog
format: https://keepachangelog.com/), or apply the 'skip-changelog'
label if this is a revert / hotfix / infrastructure-only PR.
EOF
exit 2
