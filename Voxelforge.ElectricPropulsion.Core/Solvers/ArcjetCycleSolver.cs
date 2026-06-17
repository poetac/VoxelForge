// ArcjetCycleSolver.cs — top-level Arcjet cycle solver.
//
// Mirror of HetCycleSolver for the Arcjet variant. Wraps
// MaeckerKovityaArcModel with the design/conditions API so
// ElectricPropulsionOptimization.GenerateWith can dispatch to either
// the resistojet, HET, or arcjet pipeline via design.Kind.

using System;
using Voxelforge.ElectricPropulsion.Plasma;

namespace Voxelforge.ElectricPropulsion.Solvers;

/// <summary>
/// Aggregated Arcjet cycle result. Carries both the raw thermal-arc-model
/// outputs and the <see cref="ArcjetPlasmaState"/> snapshot consumed by
/// gates and reporting.
/// </summary>
/// <param name="Maecker">Raw Maecker-Kovitya thermal-arc model output.</param>
/// <param name="PlasmaState">Strongly-typed plasma-state record (mirrors the model output).</param>
public sealed record ArcjetCycleResult(
    MaeckerKovityaResult Maecker,
    ArcjetPlasmaState    PlasmaState);

/// <summary>
/// Arcjet cycle solver. Validates design inputs, calls
/// <see cref="MaeckerKovityaArcModel.Solve"/>, and packages the result with
/// a typed <see cref="ArcjetPlasmaState"/>.
/// </summary>
public static class ArcjetCycleSolver
{
    /// <summary>
    /// Solve the Arcjet cycle for one (design, conditions) pair.
    /// </summary>
    /// <param name="design">
    /// Engine design — must have <see cref="ElectricPropulsionEngineDesign.Kind"/>
    /// set to <see cref="ElectricPropulsionEngineKind.Arcjet"/> and the 3
    /// arcjet-specific fields populated (NaN sentinels indicate a non-arcjet design).
    /// Reuses <see cref="ElectricPropulsionEngineDesign.PropellantMassFlow_kgs"/>,
    /// <see cref="ElectricPropulsionEngineDesign.NozzleThroatRadius_mm"/>,
    /// <see cref="ElectricPropulsionEngineDesign.HeaterChamberLength_mm"/>, and
    /// <see cref="ElectricPropulsionEngineDesign.HeaterChamberRadius_mm"/>
    /// from the resistojet shape per ADR-029 D3 (single design record, kind-
    /// discriminated fields). The "HeaterChamber" naming becomes
    /// "AnodeChamber" semantically for arcjet but the underlying geometry
    /// is identical (refractory cylindrical chamber upstream of CD nozzle).
    /// </param>
    /// <param name="conditions">
    /// Operating conditions. <see cref="ResistojetConditions"/> is reused
    /// per ADR-029 D3. <see cref="ResistojetConditions.BusPower_W_avail"/>
    /// is the binding constraint (arcs of higher V_arc × I_arc are
    /// rejected at SA bind time).
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="design"/> or <paramref name="conditions"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="design"/>'s Kind is not Arcjet or when
    /// any required arcjet-specific field is NaN (categorically malformed).
    /// </exception>
    public static ArcjetCycleResult Solve(
        ElectricPropulsionEngineDesign design,
        ResistojetConditions conditions)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(conditions);
        if (design.Kind != ElectricPropulsionEngineKind.Arcjet)
            throw new ArgumentException(
                $"ArcjetCycleSolver.Solve called with Kind={design.Kind}; expected Arcjet.",
                nameof(design));

        // NaN-trap: every arcjet field must be populated.
        if (double.IsNaN(design.ArcVoltage_V) ||
            double.IsNaN(design.ArcCurrent_A) ||
            double.IsNaN(design.ArcGap_mm) ||
            double.IsNaN(design.PropellantMassFlow_kgs) ||
            double.IsNaN(design.NozzleThroatRadius_mm) ||
            double.IsNaN(design.HeaterChamberLength_mm) ||
            double.IsNaN(design.HeaterChamberRadius_mm))
        {
            throw new ArgumentException(
                "Arcjet design has NaN required field(s); populate ArcVoltage_V, " +
                "ArcCurrent_A, ArcGap_mm, PropellantMassFlow_kgs, NozzleThroatRadius_mm, " +
                "HeaterChamberLength_mm, HeaterChamberRadius_mm.",
                nameof(design));
        }

        // ArcjetThermalEfficiency: NaN means "use the cluster anchor".
        double etaThermal = double.IsNaN(design.ArcjetThermalEfficiency)
            ? MaeckerKovityaArcModel.DefaultThermalEfficiency
            : design.ArcjetThermalEfficiency;

        var maecker = MaeckerKovityaArcModel.Solve(
            arcVoltage_V:           design.ArcVoltage_V,
            arcCurrent_A:           design.ArcCurrent_A,
            arcGap_mm:              design.ArcGap_mm,
            propellantMassFlow_kgs: design.PropellantMassFlow_kgs,
            nozzleThroatRadius_mm:  design.NozzleThroatRadius_mm,
            chamberLength_mm:       design.HeaterChamberLength_mm,
            chamberRadius_mm:       design.HeaterChamberRadius_mm,
            thermalEfficiency:      etaThermal);

        var plasmaState = new ArcjetPlasmaState(
            IonExitVelocity_ms:           maecker.ExitVelocity_ms,
            BeamCurrent_A:                design.ArcCurrent_A,           // arcjet has no neutraliser path
            PlumeDivergenceHalfAngle_rad: maecker.PlumeDivergenceHalfAngle_rad,
            ArcVoltage_V:                 design.ArcVoltage_V,
            ArcCurrent_A:                 design.ArcCurrent_A,
            ThermalEfficiency:            etaThermal,
            ArcPower_W:                   maecker.ArcPower_W,
            AnodeWallTemp_K:              maecker.AnodeWallTemp_K);

        return new ArcjetCycleResult(maecker, plasmaState);
    }
}
