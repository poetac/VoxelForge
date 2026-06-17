#!/usr/bin/env bash
# B3 (2026-04-25): fail PR if FeasibilityGate.cs adds a new ConstraintId
# without updating BOTH ADR-009 (the gate inventory ADR) AND GATES.md
# (the user-facing gate reference). Forcing function for ADR-009 §discipline.
#
# Usage:
#   .github/scripts/check-gate-census.sh [base-ref]
# Default base-ref is `origin/main`.
#
# Exit codes:
#   0 — no new ConstraintIds OR every new ID is documented in both files
#   1 — at least one new ConstraintId missing from ADR-009 or GATES.md
set -euo pipefail

BASE="${1:-origin/main}"
GATE_FILE="Voxelforge.Core/Optimization/FeasibilityGate.cs"
ADR_FILE="Voxelforge/docs/ADR/ADR-009-feasibility-gates.md"
GATES_DOC="Voxelforge/docs/GATES.md"

for f in "$GATE_FILE" "$ADR_FILE" "$GATES_DOC"; do
  if [[ ! -f "$f" ]]; then
    echo "WARN: $f not found at HEAD; skipping gate-census check."
    exit 0
  fi
done

# Extract ConstraintId string literals from the gate file.
extract_ids() {
  grep -oE 'ConstraintId[[:space:]]*:[[:space:]]*"[A-Z_]+"' "$1" \
    | grep -oE '"[A-Z_]+"' \
    | tr -d '"' \
    | sort -u
}

CURRENT_IDS=$(extract_ids "$GATE_FILE")
BASE_IDS=$(git show "$BASE:$GATE_FILE" 2>/dev/null | extract_ids /dev/stdin || echo "")

# Set difference: in CURRENT, not in BASE.
NEW_IDS=$(comm -23 <(echo "$CURRENT_IDS") <(echo "$BASE_IDS"))

if [[ -z "${NEW_IDS//[[:space:]]/}" ]]; then
  echo "OK: no new gate IDs added vs $BASE."
  exit 0
fi

echo "New gate IDs detected:"
echo "$NEW_IDS" | sed 's/^/  /'
echo ""

MISSING=()
while IFS= read -r id; do
  [[ -z "$id" ]] && continue
  if ! grep -q -F "$id" "$ADR_FILE"; then
    MISSING+=("$id missing from $ADR_FILE")
  fi
  if ! grep -q -F "$id" "$GATES_DOC"; then
    MISSING+=("$id missing from $GATES_DOC")
  fi
done <<< "$NEW_IDS"

if [[ ${#MISSING[@]} -gt 0 ]]; then
  cat <<EOF
ERROR: new ConstraintIds added to $GATE_FILE without corresponding
documentation entries:

EOF
  printf '  %s\n' "${MISSING[@]}"
  cat <<EOF

Per ADR-009 (feasibility-gate discipline): every new gate must appear
in both:
  • $ADR_FILE — the architectural ADR with provenance + citation
  • $GATES_DOC — the user-facing gate reference

Add the new ID to both files and retry.
EOF
  exit 1
fi

echo "OK: all new gate IDs documented in both ADR-009 and GATES.md."
exit 0
