// CentrifugalCompressorDesign.cs — Sprint CMP.W1 centrifugal compressor
// design record.
//
// Sized to bracket the GE T56-A-15 turboprop axial compressor (Pratio
// 9.5, η_polytropic ≈ 0.85) and the GT3582R turbocharger (Pratio 2.5,
// η ≈ 0.74). The Wave-1 model treats the compressor as a black-box
// "isentropic-then-corrected" stage; per-stage / per-impeller geometry
// is deferred to CMP.W2.

using System;

namespace Voxelforge.Compressor;

/// <summary>
/// Design parameters for a centrifugal compressor stage (Sprint CMP.W1
/// scaffold). Standalone — does not integrate with the
/// <c>IEngine&lt;,,&gt;</c> stack yet.
/// </summary>
/// <param name="Kind">Compressor topology.</param>
/// <param name="MassFlow_kgs">Inlet mass flow ṁ [kg/s].</param>
/// <param name="InletTotalTemperature_K">T_t1 [K].</param>
/// <param name="InletTotalPressure_Pa">P_t1 [Pa].</param>
/// <param name="PressureRatio">π_c = P_t2 / P_t1 [-].</param>
/// <param name="IsentropicEfficiency">η_isentropic ∈ (0, 1] [-]. Cluster
/// 0.74-0.85 for centrifugal stages.</param>
/// <param name="WorkingGasGamma">γ = cp / cv [-]. 1.40 for cold-side
/// air; 1.33 for hot combustion-product mixtures.</param>
/// <param name="WorkingGasSpecificHeat_J_kgK">cp [J/(kg·K)]. 1005 for
/// air; 1148 for hot air at 1500 K; 1100 for combustion products.</param>
internal sealed record CentrifugalCompressorDesign(
    CompressorKind Kind,
    double MassFlow_kgs,
    double InletTotalTemperature_K,
    double InletTotalPressure_Pa,
    double PressureRatio,
    double IsentropicEfficiency,
    double WorkingGasGamma,
    double WorkingGasSpecificHeat_J_kgK)
{
    /// <summary>Validate structural self-consistency of the design record.</summary>
    public void ValidateSelf()
    {
        if (Kind == CompressorKind.None)
            throw new ArgumentException(
                $"Kind must be Centrifugal or AxialFlow; got {Kind}.", nameof(Kind));
        if (MassFlow_kgs <= 0)
            throw new ArgumentException("MassFlow_kgs must be > 0.",
                nameof(MassFlow_kgs));
        if (InletTotalTemperature_K <= 0)
            throw new ArgumentException("InletTotalTemperature_K must be > 0.",
                nameof(InletTotalTemperature_K));
        if (InletTotalPressure_Pa <= 0)
            throw new ArgumentException("InletTotalPressure_Pa must be > 0.",
                nameof(InletTotalPressure_Pa));
        if (PressureRatio <= 1.0)
            throw new ArgumentException(
                $"PressureRatio must be > 1.0; got {PressureRatio}. "
              + "A compressor cannot reduce pressure; use a turbine for that.",
                nameof(PressureRatio));
        if (IsentropicEfficiency <= 0 || IsentropicEfficiency > 1.0)
            throw new ArgumentException(
                "IsentropicEfficiency must be in (0, 1].",
                nameof(IsentropicEfficiency));
        if (WorkingGasGamma <= 1.0 || WorkingGasGamma > 2.0)
            throw new ArgumentException(
                $"WorkingGasGamma must be in (1, 2]; got {WorkingGasGamma}.",
                nameof(WorkingGasGamma));
        if (WorkingGasSpecificHeat_J_kgK <= 0)
            throw new ArgumentException(
                "WorkingGasSpecificHeat_J_kgK must be > 0.",
                nameof(WorkingGasSpecificHeat_J_kgK));
    }
}
