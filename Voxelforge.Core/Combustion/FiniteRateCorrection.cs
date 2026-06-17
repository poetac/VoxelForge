// OOB-9 (issue #344): finite-rate (dissociation) Isp correction near throat.
// Bilinear table on (Pc_MPa, MR): correction factor ∈ [0.96, 1.00].
// CEA-calibrated at three Pc anchors (3, 8, 20 MPa) and one MR per pair.
// Off-table MR: clamp to the calibrated MR (only one anchor per pair).
// Off-table Pc: clamp to nearest anchor (3 or 20 MPa).
// Default false preserves bit-identical legacy behaviour.
using System;
using System.Collections.Generic;

namespace Voxelforge.Combustion;

internal static class FiniteRateCorrection
{
    // Calibration table: (PropellantPair, Pc_MPa) → correction factor.
    // Anchored at single calibrated MR per pair (clamp off-MR to this anchor).
    //   LOX/CH4 MR 3.5: Pc=3→0.985, Pc=8→0.990, Pc=20→0.995
    //   LOX/H2  MR 6.0: Pc=3→0.973, Pc=8→0.980, Pc=20→0.986
    //   LOX/RP1 MR 2.8: Pc=3→0.987, Pc=8→0.991, Pc=20→0.994
    private static readonly Dictionary<PropellantPair, (double[] PcMPa, double[] Factor)> _table =
        new()
        {
            [PropellantPair.LOX_CH4] = (new[] { 3.0, 8.0, 20.0 }, new[] { 0.985, 0.990, 0.995 }),
            [PropellantPair.LOX_H2]  = (new[] { 3.0, 8.0, 20.0 }, new[] { 0.973, 0.980, 0.986 }),
            [PropellantPair.LOX_RP1] = (new[] { 3.0, 8.0, 20.0 }, new[] { 0.987, 0.991, 0.994 }),
        };

    /// <summary>
    /// Dissociation correction factor for vacuum Isp, in [0.96, 1.00].
    /// Bilinear interpolation on Pc_Pa; single calibrated MR per pair.
    /// Returns 1.0 for unknown propellant pairs (no penalty).
    /// </summary>
    public static double DissociationCorrectionFactor(
        PropellantPair pair,
        double chamberPressure_Pa,
        double mixtureRatio)
    {
        _ = mixtureRatio; // MR axis has only one calibrated point per pair; clamped.

        if (!_table.TryGetValue(pair, out var entry))
            return 1.0;

        double pc_MPa = chamberPressure_Pa / 1e6;
        double[] anchors = entry.PcMPa;
        double[] factors = entry.Factor;

        if (pc_MPa <= anchors[0])
            return factors[0];
        if (pc_MPa >= anchors[^1])
            return factors[^1];

        // Linear search over 3 anchors — fast for N=3.
        for (int i = 0; i < anchors.Length - 1; i++)
        {
            if (pc_MPa <= anchors[i + 1])
            {
                double t = (pc_MPa - anchors[i]) / (anchors[i + 1] - anchors[i]);
                return factors[i] + t * (factors[i + 1] - factors[i]);
            }
        }

        return factors[^1];
    }
}
