// ChamberAnalyticalBuilder.cs — Pure-math analytical mass / cost /
// bounding-box estimate for the chamber, no voxel allocation.
//
// Sprint A-3 / ADR-021 (2026-04-30): extracted from
// `Voxelforge.Voxels/Geometry/ChamberVoxelBuilder.cs` into Core because
// the calculation is pure C# (no PicoGK references). Keeping it in Core
// lets the orchestrators consume it from a Core-side default
// `IVoxelGenerator` implementation without round-tripping through the
// App-side adapter for the bench-SA / `voxelforge-eval` paths.
//
// The PicoGK-using full-voxel builder
// (<see cref="ChamberVoxelBuilder.Build"/>) remains in Voxels.

using Voxelforge.HeatTransfer;

namespace Voxelforge.Geometry;

public static class ChamberAnalyticalBuilder
{
    /// <summary>
    /// TIER A.2 (2026-04-21): fast analytical mass/cost estimate without
    /// any PicoGK voxel ops. Consumed by parallel SA where voxel ops
    /// serialise onto the task thread and are far too expensive for a
    /// batch candidate evaluation. The returned
    /// <see cref="ChamberGeometryResult"/>'s <c>Voxels</c> field is
    /// <c>null!</c>; downstream scoring only reads mass / cost /
    /// bounding-box and is safe.
    /// </summary>
    public static ChamberGeometryResult BuildAnalytical(ChamberBuildOptions opt)
    {
        var contour = opt.Contour;
        var ch = opt.Channels;
        var material = opt.MaterialForMass ?? WallMaterials.CuCrZr;

        // Analytical solid volume via conical-frustum sum along the contour,
        // minus channel volume (rectangular cross-section swept axially).
        // Ablative-only builds (SkipChannelGeneration) zero the channel term
        // and shrink the outer shell by the channel-height contribution so
        // the wall stack is exactly wall + jacket.
        double solidVol_mm3 = 0;
        double innerSurf_mm2 = 0;
        // Z1 hot-fix / Track B closed-loop (2026-04-28): respect per-station
        // wall profile when caller passed one. Null OR length mismatch ⇒
        // uniform fallback (bit-identical).
        bool useWallProfile = opt.GasSideWallProfile_mm is not null
                           && opt.GasSideWallProfile_mm.Count == contour.Stations.Length;
        double GasSideWallAt(int idx)
            => useWallProfile ? opt.GasSideWallProfile_mm![idx] : ch.GasSideWallThickness_mm;
        for (int i = 0; i < contour.Stations.Length - 1; i++)
        {
            var a = contour.Stations[i];
            var b = contour.Stations[i + 1];
            double dx = b.X_mm - a.X_mm;
            double r_in_a  = a.R_mm;
            double r_in_b  = b.R_mm;
            // Per-station wall (i + 1 averaged with i to track the frustum
            // segment fairly when the profile gradient is non-zero).
            double t_wall  = 0.5 * (GasSideWallAt(i) + GasSideWallAt(i + 1));
            double t_jkt   = opt.OuterJacketThickness_mm;
            // Channel height at this axial position — linear interp.
            double t = (double)i / System.Math.Max(contour.Stations.Length - 2, 1);
            double h_ch_mm = t <= 0.5
                ? ch.ChannelHeightAtChamber_mm + 2 * t * (ch.ChannelHeightAtThroat_mm - ch.ChannelHeightAtChamber_mm)
                : ch.ChannelHeightAtThroat_mm  + 2 * (t - 0.5) * (ch.ChannelHeightAtExit_mm - ch.ChannelHeightAtThroat_mm);
            double h_shell_mm = opt.SkipChannelGeneration ? 0.0 : h_ch_mm;

            double r_out_a = r_in_a + t_wall + h_shell_mm + t_jkt;
            double r_out_b = r_in_b + t_wall + h_shell_mm + t_jkt;
            // Outer frustum volume.
            double vOuter = System.Math.PI / 3.0 * dx
                          * (r_out_a * r_out_a + r_out_a * r_out_b + r_out_b * r_out_b);
            // Inner (gas-side) frustum volume.
            double vInner = System.Math.PI / 3.0 * dx
                          * (r_in_a * r_in_a + r_in_a * r_in_b + r_in_b * r_in_b);
            double vChannels = 0;
            if (!opt.SkipChannelGeneration)
            {
                // Channels: rectangular, N of them, width = pitch − rib at r_mid.
                double r_ch_mid = 0.5 * (r_in_a + r_in_b) + t_wall + 0.5 * h_ch_mm;
                double pitch_mm = 2.0 * System.Math.PI * r_ch_mid / System.Math.Max(ch.ChannelCount, 1);
                double w_ch = System.Math.Max(pitch_mm - ch.RibThickness_mm, 0.3);
                vChannels = ch.ChannelCount * w_ch * h_ch_mm * dx;
            }

            solidVol_mm3 += System.Math.Max(vOuter - vInner - vChannels, 0);

            double r_avg = 0.5 * (r_in_a + r_in_b);
            double slant = System.Math.Sqrt(dx * dx + (r_in_b - r_in_a) * (r_in_b - r_in_a));
            innerSurf_mm2 += 2.0 * System.Math.PI * r_avg * slant;
        }

        double vol_cm3 = solidVol_mm3 / 1000.0;
        double mass_g  = vol_cm3 * (material.Density_kgm3 / 1000.0);
        double cost    = vol_cm3 * material.PrintCostPerCm3_USD;
        double totalLen = contour.TotalLength_mm;
        double h_exit_mm = opt.SkipChannelGeneration ? 0.0 : ch.ChannelHeightAtExit_mm;
        double totalDia = 2.0 * (contour.Stations[^1].R_mm + ch.GasSideWallThickness_mm
                                 + h_exit_mm + opt.OuterJacketThickness_mm);

        return new ChamberGeometryResult(
            Voxels:              null!,   // physics-only: mesh intentionally absent
            SolidVolume_mm3:     solidVol_mm3,
            InnerSurfaceArea_mm2: innerSurf_mm2,
            OuterJacketThickness_mm: opt.OuterJacketThickness_mm,
            TotalMass_g:          mass_g,
            PrintedCost_USD:      cost,
            BoundingLength_mm:    totalLen,
            BoundingDiameter_mm:  totalDia,
            Description:          $"Physics-only stub: {totalLen:F1} × Ø{totalDia:F1} mm, {mass_g:F1} g.",
            InjectorSTLMessage:   "");
    }
}
