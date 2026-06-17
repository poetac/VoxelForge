// HawtDesign.cs — Sprint WT.W1 horizontal-axis wind turbine design record.
//
// Sized to bracket the NREL 5 MW reference + IEA 15 MW reference + the
// small-residential cluster. The Wave-1 scaffold is a pure aerodynamic
// envelope record — structural / aeroelastic / electrical-grid fields
// are deferred to WT.W2+.
//
// Reference: Jonkman J., Butterfield S., Musial W., Scott G. (2009).
//   "Definition of a 5-MW Reference Wind Turbine for Offshore System
//    Development." NREL/TP-500-38060.

using System;

namespace Voxelforge.WindTurbine;

/// <summary>
/// Design parameters for a horizontal-axis wind turbine (Sprint WT.W1
/// scaffold). Standalone — does not integrate with the IEngine&lt;,,&gt;
/// stack yet (deferred to a future WT.W2 sprint).
/// </summary>
/// <param name="Kind">Sub-variant — <see cref="WindTurbineKind.HorizontalAxis"/> for Wave-1.</param>
/// <param name="RotorRadius_m">Rotor radius R [m] (tip-to-hub).</param>
/// <param name="BladeCount">Number of blades B [-] (3 dominates commercial; 2 sometimes).</param>
/// <param name="HubHeight_m">Hub height above ground / sea surface [m].</param>
/// <param name="DesignWindSpeed_ms">Rated / design wind speed V_design [m/s] — used as the BEM operating point.</param>
/// <param name="DesignTipSpeedRatio">Design tip-speed ratio λ = ωR / V [-].</param>
/// <param name="GearboxAndGeneratorEfficiency">η_drivetrain [-] — combined gearbox + generator + power electronics.</param>
/// <param name="CutInWindSpeed_ms">Wind speed below which the turbine is parked [m/s].</param>
/// <param name="CutOutWindSpeed_ms">Wind speed above which the turbine is parked for safety [m/s].</param>
internal sealed record HawtDesign(
    WindTurbineKind Kind,
    double RotorRadius_m,
    int    BladeCount,
    double HubHeight_m,
    double DesignWindSpeed_ms,
    double DesignTipSpeedRatio,
    double GearboxAndGeneratorEfficiency,
    double CutInWindSpeed_ms,
    double CutOutWindSpeed_ms)
{
    /// <summary>Rotor swept area A = π · R² [m²].</summary>
    public double SweptArea_m2 => Math.PI * RotorRadius_m * RotorRadius_m;

    /// <summary>Validate structural self-consistency of the design record.</summary>
    /// <exception cref="ArgumentException">When any dimension is non-positive,
    /// blade count is out of [1, 6], η_drivetrain is outside (0, 1], or the
    /// cut-in / cut-out / design speeds aren't in monotonic order.</exception>
    public void ValidateSelf()
    {
        if (Kind == WindTurbineKind.None)
            throw new ArgumentException(
                $"Kind must be HorizontalAxis or VerticalAxis; got {Kind}.", nameof(Kind));
        if (RotorRadius_m <= 0)
            throw new ArgumentException("RotorRadius_m must be > 0.", nameof(RotorRadius_m));
        if (BladeCount < 1 || BladeCount > 6)
            throw new ArgumentException(
                $"BladeCount must be in [1, 6]; got {BladeCount}.", nameof(BladeCount));
        if (HubHeight_m <= 0)
            throw new ArgumentException("HubHeight_m must be > 0.", nameof(HubHeight_m));
        if (HubHeight_m <= RotorRadius_m)
            throw new ArgumentException(
                $"HubHeight_m ({HubHeight_m:F1}) must exceed RotorRadius_m "
              + $"({RotorRadius_m:F1}); otherwise blade tips strike the ground.",
                nameof(HubHeight_m));
        if (DesignWindSpeed_ms <= 0)
            throw new ArgumentException("DesignWindSpeed_ms must be > 0.",
                nameof(DesignWindSpeed_ms));
        if (DesignTipSpeedRatio <= 0)
            throw new ArgumentException("DesignTipSpeedRatio must be > 0.",
                nameof(DesignTipSpeedRatio));
        if (GearboxAndGeneratorEfficiency <= 0 || GearboxAndGeneratorEfficiency > 1.0)
            throw new ArgumentException(
                "GearboxAndGeneratorEfficiency must be in (0, 1].",
                nameof(GearboxAndGeneratorEfficiency));
        if (CutInWindSpeed_ms <= 0)
            throw new ArgumentException("CutInWindSpeed_ms must be > 0.",
                nameof(CutInWindSpeed_ms));
        if (CutOutWindSpeed_ms <= CutInWindSpeed_ms)
            throw new ArgumentException(
                $"CutOutWindSpeed_ms ({CutOutWindSpeed_ms:F1}) must exceed "
              + $"CutInWindSpeed_ms ({CutInWindSpeed_ms:F1}).",
                nameof(CutOutWindSpeed_ms));
        if (DesignWindSpeed_ms < CutInWindSpeed_ms || DesignWindSpeed_ms > CutOutWindSpeed_ms)
            throw new ArgumentException(
                $"DesignWindSpeed_ms ({DesignWindSpeed_ms:F1}) must be in the "
              + $"[CutIn, CutOut] = [{CutInWindSpeed_ms:F1}, {CutOutWindSpeed_ms:F1}] band.",
                nameof(DesignWindSpeed_ms));
    }
}
