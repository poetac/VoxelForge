#!/usr/bin/env bash
# Reapplies the original voxelforge-old label colors+descriptions to poetac/voxelforge.
# Requires: gh auth login (repo scope). Run once after the issue restore.
set -euo pipefail
REPO=poetac/voxelforge

gh label create "analyzer" --repo "$REPO" --color "ededed" --description "" --force
gh label create "audit:pending" --repo "$REPO" --color "C5DEF5" --description "Awaiting audit decision" --force
gh label create "breaking-api" --repo "$REPO" --color "B60205" --description "Changes a public API surface" --force
gh label create "bug" --repo "$REPO" --color "d73a4a" --description "Something isn't working" --force
gh label create "documentation" --repo "$REPO" --color "0075ca" --description "Improvements or additions to documentation" --force
gh label create "enhancement" --repo "$REPO" --color "a2eeef" --description "New feature or request" --force
gh label create "gate-added" --repo "$REPO" --color "5319E7" --description "Adds a new feasibility gate" --force
gh label create "good first issue" --repo "$REPO" --color "7057ff" --description "Good for newcomers" --force
gh label create "help wanted" --repo "$REPO" --color "008672" --description "Extra attention is needed" --force
gh label create "optimization" --repo "$REPO" --color "ededed" --description "" --force
gh label create "priority:critical" --repo "$REPO" --color "B60205" --description "Blocks team or affects correctness — fix now" --force
gh label create "priority:high" --repo "$REPO" --color "D93F0B" --description "Current sprint or near-term planned work" --force
gh label create "priority:normal" --repo "$REPO" --color "0E8A16" --description "Backlog — pick up when sprint allows" --force
gh label create "risk:high" --repo "$REPO" --color "B60205" --description "High-risk change — careful review needed" --force
gh label create "risk:low" --repo "$REPO" --color "0E8A16" --description "Low-risk change (docs, tests, mechanical refactor)" --force
gh label create "risk:medium" --repo "$REPO" --color "FBCA04" --description "Medium-risk change" --force
gh label create "size:large" --repo "$REPO" --color "F4A6A6" --description "Estimated effort >3 days; consider splitting" --force
gh label create "size:medium" --repo "$REPO" --color "F9D6A0" --description "Estimated effort 1-3 days" --force
gh label create "size:small" --repo "$REPO" --color "98D8C8" --description "Estimated effort 2-8 hours (one day or less)" --force
gh label create "size:tiny" --repo "$REPO" --color "C2E0C6" --description "Estimated effort ≤2 hours" --force
gh label create "testing" --repo "$REPO" --color "ededed" --description "" --force
gh label create "tooling" --repo "$REPO" --color "ededed" --description "" --force
gh label create "track:benchmarking" --repo "$REPO" --color "BFD4F2" --description "Benchmarking expansion (BB-3..BB-6)" --force
gh label create "track:ci" --repo "$REPO" --color "9B59B6" --description "CI workflows, runner health, automation" --force
gh label create "track:docs" --repo "$REPO" --color "FBCF4D" --description "Decision framework, onboarding surface, cross-doc consistency" --force
gh label create "track:optimization-infra" --repo "$REPO" --color "5319E7" --description "Optimization infrastructure (T1.x / T2.x)" --force
gh label create "track:physics-cascade" --repo "$REPO" --color "D73A4A" --description "Physics-correctness cascade (audit-driven)" --force
gh label create "track:scope-expansion" --repo "$REPO" --color "FBCA04" --description "Long-term scope expansion staircase" --force
gh label create "track:testing" --repo "$REPO" --color "E6B8D5" --description "Test infrastructure, smoke tests, conformance, fixtures" --force
