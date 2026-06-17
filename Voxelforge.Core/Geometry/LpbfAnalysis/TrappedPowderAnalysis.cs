// TrappedPowderAnalysis.cs — Sprint 27 (2026-04-23): flood-fill from the
// bounding-box exterior through the part's void space; anything unreached
// is a trapped-powder pocket.
//
// The algorithm
// ─────────────
// 1. Build a 3D bool "reachable" grid the same shape as the occupancy grid.
// 2. Seed a BFS queue with every VOID voxel that touches the bounding-box
//    boundary. These are "outside" voxels — powder can evacuate through
//    them as the part is tumbled on the build plate.
// 3. Additionally, seed voxels around every supplied `OpeningPort` — feed
//    inlets, manifold outlets, drain holes. The ports let the flood-fill
//    reach pockets that would otherwise be walled off by the printed
//    envelope (e.g. the chamber bore sitting entirely inside a
//    monolithic shell).
// 4. BFS with 6-connected (face-adjacent) neighbours; never cross a solid
//    voxel.
// 5. Any remaining unreached void voxel belongs to a trapped-powder
//    region. Connected-component labelling on the leftover void voxels
//    surfaces one violation per distinct pocket.
//
// Why 6-connected (not 26): LPBF powder is a granular medium with particle
// size 15-45 µm — typical voxel sizes in this project are 0.4-1.0 mm. A
// powder particle can only travel through a face-neighbour opening, not a
// diagonal one (surface tension in a cake of partially-sintered particles
// blocks diagonal leakage). Conservative choice; if a real test shows it
// over-reports, promote to 26-connected later.
//
// Performance: the flood-fill is O(N) where N = grid-size. At 1 mm voxels
// on a 300 mm-cube build envelope that's 2.7e7 voxels, flood-fill in a
// couple seconds — acceptable as an opt-in analysis.

using System;
using System.Collections.Generic;
using System.Numerics;

namespace Voxelforge.Geometry.LpbfAnalysis;

/// <summary>One trapped-powder pocket: the connected component of void
/// voxels that flood-fill couldn't reach from the bounding-box exterior
/// or any supplied opening.</summary>
public readonly record struct TrappedPowderPocket(
    int     VoxelCount,
    double  Volume_mm3,
    Vector3 CentroidWorld_mm);

/// <summary>Output of <see cref="TrappedPowderAnalysis.Analyze"/>.</summary>
public sealed record TrappedPowderReport(
    int                                                         PocketCount,
    double                                                      TotalTrappedVolume_mm3,
    System.Collections.Generic.IReadOnlyList<TrappedPowderPocket> Pockets)
{
    /// <summary>True when no trapped-powder pocket was detected.</summary>
    public bool IsPrintable => PocketCount == 0;
}

/// <summary>
/// Sprint 27 (2026-04-23): flood-fill trapped-powder detector. See file
/// comment for algorithm details.
/// </summary>
public static class TrappedPowderAnalysis
{
    public static TrappedPowderReport Analyze(
        VoxelFieldSnapshot                 voxels,
        IEnumerable<OpeningPort>?          openings = null,
        // Sprint 30 (2026-04-24, PH-3) — minimum pocket volume to
        // surface as a trapped-powder flag. Defaults to 0.0 for
        // back-compat; callers should pass
        // `LpbfMaterialProfile.MinFlaggedPocketVolume_mm3` (default
        // 5 mm³) so single-voxel jitter doesn't surface as spurious
        // violations.
        double                             minFlaggedPocketVolume_mm3 = 0.0)
    {
        if (voxels is null) throw new ArgumentNullException(nameof(voxels));

        int ni = voxels.SizeI, nj = voxels.SizeJ, nk = voxels.SizeK;
        var solid = voxels.Occupancy;
        var reached = new bool[ni, nj, nk];

        // ── Seed: every void voxel that touches the bounding-box boundary
        var queue = new Queue<(int i, int j, int k)>();
        for (int i = 0; i < ni; i++)
        for (int j = 0; j < nj; j++)
        for (int k = 0; k < nk; k++)
        {
            bool onBoundary = i == 0 || i == ni - 1
                           || j == 0 || j == nj - 1
                           || k == 0 || k == nk - 1;
            if (onBoundary && !solid[i, j, k])
            {
                reached[i, j, k] = true;
                queue.Enqueue((i, j, k));
            }
        }

        // ── Seed: every voxel within radius of an opening
        if (openings is not null)
        {
            double vs = voxels.VoxelSize_mm;
            foreach (var port in openings)
            {
                // Transform world-space centre into lattice coordinates.
                Vector3 local = (port.Center - voxels.Origin) / (float)vs;
                int ci = (int)Math.Round(local.X - 0.5);
                int cj = (int)Math.Round(local.Y - 0.5);
                int ck = (int)Math.Round(local.Z - 0.5);
                int r = Math.Max(1, (int)Math.Ceiling(port.Radius_mm / vs));
                double r2Vox = (double)r * r;
                for (int di = -r; di <= r; di++)
                for (int dj = -r; dj <= r; dj++)
                for (int dk = -r; dk <= r; dk++)
                {
                    if (di*di + dj*dj + dk*dk > r2Vox) continue;
                    int ii = ci + di, jj = cj + dj, kk = ck + dk;
                    if (!voxels.InBounds(ii, jj, kk)) continue;
                    if (solid[ii, jj, kk]) continue;
                    if (reached[ii, jj, kk]) continue;
                    reached[ii, jj, kk] = true;
                    queue.Enqueue((ii, jj, kk));
                }
            }
        }

        // ── BFS through void space, 6-connected
        int[] di6 = {  1, -1,  0,  0,  0,  0 };
        int[] dj6 = {  0,  0,  1, -1,  0,  0 };
        int[] dk6 = {  0,  0,  0,  0,  1, -1 };
        while (queue.Count > 0)
        {
            var (i, j, k) = queue.Dequeue();
            for (int n = 0; n < 6; n++)
            {
                int ii = i + di6[n], jj = j + dj6[n], kk = k + dk6[n];
                if (!voxels.InBounds(ii, jj, kk)) continue;
                if (solid[ii, jj, kk]) continue;
                if (reached[ii, jj, kk]) continue;
                reached[ii, jj, kk] = true;
                queue.Enqueue((ii, jj, kk));
            }
        }

        // ── Connected-component labelling on unreached void voxels
        var pockets = new List<TrappedPowderPocket>();
        var componentLabel = new int[ni, nj, nk];
        int nextLabel = 0;
        for (int i = 0; i < ni; i++)
        for (int j = 0; j < nj; j++)
        for (int k = 0; k < nk; k++)
        {
            if (solid[i, j, k] || reached[i, j, k] || componentLabel[i, j, k] != 0)
                continue;
            nextLabel++;
            int count = 0;
            double sx = 0, sy = 0, sz = 0;
            var q2 = new Queue<(int, int, int)>();
            q2.Enqueue((i, j, k));
            componentLabel[i, j, k] = nextLabel;
            while (q2.Count > 0)
            {
                var (ai, aj, ak) = q2.Dequeue();
                count++;
                sx += ai + 0.5;
                sy += aj + 0.5;
                sz += ak + 0.5;
                for (int n = 0; n < 6; n++)
                {
                    int ii = ai + di6[n], jj = aj + dj6[n], kk = ak + dk6[n];
                    if (!voxels.InBounds(ii, jj, kk)) continue;
                    if (solid[ii, jj, kk]) continue;
                    if (reached[ii, jj, kk]) continue;
                    if (componentLabel[ii, jj, kk] != 0) continue;
                    componentLabel[ii, jj, kk] = nextLabel;
                    q2.Enqueue((ii, jj, kk));
                }
            }
            double vs = voxels.VoxelSize_mm;
            double vol = count * vs * vs * vs;
            Vector3 centroid = voxels.Origin + new Vector3(
                (float)(sx / count * vs),
                (float)(sy / count * vs),
                (float)(sz / count * vs));
            pockets.Add(new TrappedPowderPocket(
                VoxelCount:        count,
                Volume_mm3:        vol,
                CentroidWorld_mm:  centroid));
        }

        // Sprint 30 (PH-3) — drop sub-threshold pockets so single-voxel
        // jitter at the part boundary doesn't surface as spurious
        // TRAPPED_POWDER_REGION violations. The total volume reported
        // still includes the sub-threshold pockets so the diagnostic
        // sees the full unreached-void picture; only the per-pocket
        // list is filtered.
        var flaggedPockets = minFlaggedPocketVolume_mm3 > 0
            ? pockets.FindAll(p => p.Volume_mm3 >= minFlaggedPocketVolume_mm3)
            : pockets;

        double total = 0;
        foreach (var p in pockets) total += p.Volume_mm3;

        return new TrappedPowderReport(
            PocketCount:            flaggedPockets.Count,
            TotalTrappedVolume_mm3: total,
            Pockets:                flaggedPockets);
    }
}
