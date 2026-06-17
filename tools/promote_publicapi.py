"""Promote PublicAPI.Unshipped.txt entries into PublicAPI.Shipped.txt.

For each project:
  - Each `*REMOVED*<entry>` line in Unshipped is an instruction to drop `<entry>`
    from Shipped (after stripping the `*REMOVED*` prefix).
  - Each other non-blank, non-header line in Unshipped is an addition
    appended to Shipped.
  - Shipped is then re-sorted (header `#nullable enable` first, blank entries
    elided, remaining entries sorted by ordinal).
  - Unshipped is reset to just `#nullable enable\n`.

Run with no arguments. Operates on the two PublicAPI projects in this repo.
Idempotent: if Unshipped is already empty (just header), nothing changes.
"""

from __future__ import annotations

import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
PROJECTS = [
    REPO_ROOT / "Voxelforge.Core",
    REPO_ROOT / "Voxelforge.Voxels",
]

HEADER = "#nullable enable"


def promote(project_dir: Path) -> None:
    shipped_path = project_dir / "PublicAPI.Shipped.txt"
    unshipped_path = project_dir / "PublicAPI.Unshipped.txt"

    if not shipped_path.exists() or not unshipped_path.exists():
        print(f"  [skip] {project_dir.name}: missing PublicAPI files")
        return

    shipped = shipped_path.read_text(encoding="utf-8").splitlines()
    unshipped = unshipped_path.read_text(encoding="utf-8").splitlines()

    additions: list[str] = []
    removals: set[str] = set()

    for raw in unshipped:
        line = raw.rstrip()
        if not line or line == HEADER:
            continue
        if line.startswith("*REMOVED*"):
            removals.add(line[len("*REMOVED*"):])
        else:
            additions.append(line)

    if not additions and not removals:
        print(f"  [noop] {project_dir.name}: nothing to promote")
        return

    # Drop header + any *REMOVED* entries from shipped, then append additions.
    body = [
        line.rstrip()
        for line in shipped
        if line.rstrip() and line.rstrip() != HEADER and line.rstrip() not in removals
    ]
    body.extend(additions)
    body = sorted(set(body), key=lambda s: (s.lower(), s))

    new_shipped = "\n".join([HEADER, *body]) + "\n"
    new_unshipped = HEADER + "\n"

    shipped_path.write_text(new_shipped, encoding="utf-8")
    unshipped_path.write_text(new_unshipped, encoding="utf-8")

    print(
        f"  [done] {project_dir.name}: "
        f"+{len(additions)} additions, -{len(removals)} removals, "
        f"shipped now {len(body)} entries"
    )


def main() -> int:
    print("Promoting PublicAPI entries from Unshipped -> Shipped")
    for project in PROJECTS:
        promote(project)
    return 0


if __name__ == "__main__":
    sys.exit(main())
