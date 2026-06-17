// CentrifugalPumpDesign.cs — Sprint PMP.W1 pump design record.
//
// Sized to bracket Goulds 3196-class process pumps (Q ≈ 0.05 m³/s,
// H ≈ 50 m, η ≈ 0.75) and the SpaceX Merlin LOX turbopump (Q ≈
// 0.17 m³/s, H ≈ 1700 m at 110 bar discharge, η ≈ 0.70).

using System;

namespace Voxelforge.Pump;

/// <summary>
/// Design parameters for a single-stage centrifugal pump
/// (Sprint PMP.W1 scaffold). Standalone — does not integrate with the
/// <c>IEngine&lt;,,&gt;</c> stack yet.
/// </summary>
/// <param name="Kind">Pump topology.</param>
/// <param name="VolumetricFlowRate_m3s">Q [m³/s].</param>
/// <param name="HeadRise_m">H = ΔP / (ρ·g) [m] — the pump's effective
/// pressure rise expressed as fluid-column height.</param>
/// <param name="RotationSpeed_rpm">N [rev/min] — drives specific speed.</param>
/// <param name="OverallEfficiency">η_pump ∈ (0, 1] [-]. Cluster 0.65-
/// 0.85 for single-stage centrifugal at the BEP (best-efficiency point).</param>
/// <param name="FluidDensity_kgm3">ρ [kg/m³]. Default 1000 (fresh water).</param>
/// <param name="FluidVapourPressure_Pa">p_v [Pa] — drives NPSH_a. Default
/// 2340 Pa (saturated water at 20 °C); LOX = 100 kPa at boiling.</param>
/// <param name="InletStaticPressure_Pa">P_inlet [Pa] — at the pump suction
/// flange.</param>
/// <param name="InletElevationLift_m">z_lift [m] — vertical height of pump
/// inlet above the supply reservoir surface (positive for lift, negative
/// for flooded suction).</param>
/// <param name="InletFrictionLoss_m">h_f [m] — friction loss in the suction
/// piping (cluster mid-band 0.5-3 m for typical industrial layouts).</param>
internal sealed record CentrifugalPumpDesign(
    PumpKind Kind,
    double VolumetricFlowRate_m3s,
    double HeadRise_m,
    double RotationSpeed_rpm,
    double OverallEfficiency,
    double FluidDensity_kgm3      = 1000.0,
    double FluidVapourPressure_Pa = 2340.0,
    double InletStaticPressure_Pa = 101325.0,
    double InletElevationLift_m   = 0.0,
    double InletFrictionLoss_m    = 0.0)
{
    /// <summary>Validate structural self-consistency of the design record.</summary>
    public void ValidateSelf()
    {
        if (Kind == PumpKind.None)
            throw new ArgumentException(
                $"Kind must be Centrifugal or PositiveDisplacement; got {Kind}.",
                nameof(Kind));
        if (VolumetricFlowRate_m3s <= 0)
            throw new ArgumentException("VolumetricFlowRate_m3s must be > 0.",
                nameof(VolumetricFlowRate_m3s));
        if (HeadRise_m <= 0)
            throw new ArgumentException("HeadRise_m must be > 0.",
                nameof(HeadRise_m));
        if (RotationSpeed_rpm <= 0)
            throw new ArgumentException("RotationSpeed_rpm must be > 0.",
                nameof(RotationSpeed_rpm));
        if (OverallEfficiency <= 0 || OverallEfficiency > 1.0)
            throw new ArgumentException(
                "OverallEfficiency must be in (0, 1].", nameof(OverallEfficiency));
        if (FluidDensity_kgm3 <= 0)
            throw new ArgumentException("FluidDensity_kgm3 must be > 0.",
                nameof(FluidDensity_kgm3));
        if (FluidVapourPressure_Pa < 0)
            throw new ArgumentException("FluidVapourPressure_Pa must be ≥ 0.",
                nameof(FluidVapourPressure_Pa));
        if (InletStaticPressure_Pa <= 0)
            throw new ArgumentException("InletStaticPressure_Pa must be > 0.",
                nameof(InletStaticPressure_Pa));
        if (InletFrictionLoss_m < 0)
            throw new ArgumentException("InletFrictionLoss_m must be ≥ 0.",
                nameof(InletFrictionLoss_m));
    }
}
