// NuclearThermalConditions.cs — operating envelope for a NERVA-class NTR.
//
// Implements IEngineConditions with Family = EngineFamilies.Nuclear.
// Analogous to MarineConditions on the marine side.
// NTRs operate in vacuum (space missions); sea-level firing is for ground-test only.

using Voxelforge.Engines;

namespace Voxelforge.Nuclear;

/// <summary>
/// Operating conditions for a NERVA-class nuclear thermal rocket.
/// </summary>
/// <param name="PropellantInletTemp_K">
/// LH₂ propellant temperature at the reactor inlet [K].
/// Typical NTR ground-test value: 80–100 K (tank conditions after
/// turbopump; the fluid is warmed slightly before reaching the reactor).
/// Default: 80.0 K.
/// </param>
/// <param name="TargetDeltaV_ms">
/// Mission ΔV budget [m/s]. Used as a scoring context (higher Isp →
/// smaller propellant mass fraction for a given ΔV). Default 3000 m/s
/// (Earth-departure NTR manoeuvre).
/// </param>
public sealed record NuclearThermalConditions(
    double PropellantInletTemp_K = 80.0,
    double TargetDeltaV_ms = 3000.0) : IEngineConditions
{
    /// <inheritdoc />
    public string Family => EngineFamilies.Nuclear;

    /// <summary>Propellant mass fraction for the given ΔV and Isp (Tsiolkovsky).</summary>
    /// <param name="isp_s">Specific impulse [s].</param>
    public double PropellantMassFraction(double isp_s)
    {
        if (isp_s <= 0) return double.NaN;
        double v_e = isp_s * 9.80665;
        return 1.0 - Math.Exp(-TargetDeltaV_ms / v_e);
    }
}
