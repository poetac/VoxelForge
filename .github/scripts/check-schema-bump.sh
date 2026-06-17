#!/usr/bin/env bash
# B3 (2026-04-25): fail PR if DesignPersistence.cs is touched without
# bumping the schema version. Forcing function for ADR-009-style
# migration discipline.
#
# Usage:
#   .github/scripts/check-schema-bump.sh [base-ref]
# Default base-ref is `origin/main`.
#
# Exit codes:
#   0 — no DesignPersistence change OR change includes a version bump
#   1 — DesignPersistence changed but CurrentSchemaVersion is unchanged
set -euo pipefail

BASE="${1:-origin/main}"
DESIGN_PERSISTENCE="Voxelforge.Core/IO/DesignPersistence.cs"

if [[ ! -f "$DESIGN_PERSISTENCE" ]]; then
  echo "WARN: $DESIGN_PERSISTENCE not found at HEAD; skipping schema-bump check."
  exit 0
fi

# Did DesignPersistence.cs change relative to base?
if git diff --quiet "$BASE" -- "$DESIGN_PERSISTENCE"; then
  echo "OK: $DESIGN_PERSISTENCE unchanged vs $BASE — schema-bump check is a no-op."
  exit 0
fi

echo "$DESIGN_PERSISTENCE was modified vs $BASE; checking schema version..."

# Pull both versions of the file and extract CurrentSchemaVersion.
extract_version() {
  grep -oE 'CurrentSchemaVersion[[:space:]]*=[[:space:]]*"v[0-9]+"' "$1" \
    | head -1 \
    | grep -oE 'v[0-9]+' || echo ""
}

OLD_V=$(git show "$BASE:$DESIGN_PERSISTENCE" | extract_version /dev/stdin)
NEW_V=$(extract_version "$DESIGN_PERSISTENCE")

if [[ -z "$OLD_V" || -z "$NEW_V" ]]; then
  echo "WARN: could not parse CurrentSchemaVersion (old='$OLD_V', new='$NEW_V'). Skipping check."
  exit 0
fi

if [[ "$OLD_V" == "$NEW_V" ]]; then
  cat <<EOF
ERROR: $DESIGN_PERSISTENCE was modified but CurrentSchemaVersion did not bump.

  Base ($BASE): $OLD_V
  Head:         $NEW_V

If this change is schema-compatible (e.g. comment-only, refactor that
preserves on-disk format), confirm by adding a 'schema-noop:' line to
the PR description and re-running the check via:

  .github/scripts/check-schema-bump.sh --allow-noop

Otherwise: bump CurrentSchemaVersion to the next vN, add a migration
arm to the Migrations dictionary, and append the new version to
KnownSchemas.
EOF
  exit 1
fi

echo "OK: schema bumped $OLD_V → $NEW_V."
exit 0
