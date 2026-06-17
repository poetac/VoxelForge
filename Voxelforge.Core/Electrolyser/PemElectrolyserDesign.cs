// PemElectrolyserDesign.cs — Sprint EL.W1 PEM electrolyser stack design.
//
// Sized to bracket Nel A485 / ITM HGas-class commercial PEM
// electrolysers (~ 1-5 MW stacks running ~ 100-300 cells at ~ 200 cm²
// active area, 60-80 °C, 10-30 bar). The pillar mirrors PG.W1 in
// shape — they're the same physics in opposite directions.

using System;

namespace Voxelforge.Electrolyser;

/// <summary>
/// Design parameters for a single PEM electrolyser stack (Sprint EL.W1
/// scaffold). Standalone — does not integrate with the IEngine&lt;,,&gt;
/// stack yet (deferred to a future EL.W2 sprint).
/// </summary>
/// <param name="Kind">Sub-variant — <see cref="ElectrolyserKind.Pem"/> for Wave-1.</param>
/// <param name="CellCount">Number of series-stacked cells N [-].</param>
/// <param name="ActiveAreaPerCell_cm2">Active electrode area per cell A [cm²].</param>
/// <param name="OperatingCurrentDensity_A_cm2">Nominal current density i [A/cm²].</param>
/// <param name="OperatingTemperature_C">Stack operating temperature T [°C].</param>
/// <param name="OperatingPressure_bar">Stack operating pressure P [bar].</param>
internal sealed record PemElectrolyserDesign(
    ElectrolyserKind Kind,
    int    CellCount,
    double ActiveAreaPerCell_cm2,
    double OperatingCurrentDensity_A_cm2,
    double OperatingTemperature_C,
    double OperatingPressure_bar)
{
    /// <summary>Total active area summed across cells [cm²].</summary>
    public double TotalActiveArea_cm2 => CellCount * ActiveAreaPerCell_cm2;

    /// <summary>Validate structural self-consistency of the design record.</summary>
    public void ValidateSelf()
    {
        if (Kind != ElectrolyserKind.Pem)
            throw new ArgumentException(
                $"Wave-1 supports only Pem; got {Kind}.", nameof(Kind));
        if (CellCount <= 0)
            throw new ArgumentException("CellCount must be > 0.", nameof(CellCount));
        if (ActiveAreaPerCell_cm2 <= 0)
            throw new ArgumentException("ActiveAreaPerCell_cm2 must be > 0.",
                nameof(ActiveAreaPerCell_cm2));
        if (OperatingCurrentDensity_A_cm2 <= 0)
            throw new ArgumentException("OperatingCurrentDensity_A_cm2 must be > 0.",
                nameof(OperatingCurrentDensity_A_cm2));
        if (OperatingTemperature_C <= 0)
            throw new ArgumentException(
                "OperatingTemperature_C must be > 0 (Celsius — PEM is liquid-water-balanced).",
                nameof(OperatingTemperature_C));
        if (OperatingPressure_bar <= 0)
            throw new ArgumentException("OperatingPressure_bar must be > 0.",
                nameof(OperatingPressure_bar));
    }
}
