// PemFuelCellDesign.cs — Sprint PG.W1 PEM fuel cell stack design record.
//
// Stateless, immutable. Mirrors HybridRocketDesign + NuclearThermalDesign
// in shape. Sized to bracket the Toyota Mirai / Ballard MK-class
// commercial cluster (50-150 kW stacks running ~ 300 cells of ~ 200 cm²
// active area at ~ 80 °C and ~ 2.5 bar inlet pressure).

using System;

namespace Voxelforge.PowerGen;

/// <summary>
/// Design parameters for a single PEM fuel cell stack (Sprint PG.W1
/// scaffold). Standalone — does not integrate with the
/// <c>IEngine&lt;,,&gt;</c> stack yet (deferred to a future PG.W2 sprint).
/// </summary>
/// <param name="Kind">Sub-variant — <see cref="PowerGenKind.PemFuelCell"/> for Wave-1.</param>
/// <param name="CellCount">Number of series-stacked cells N [-].</param>
/// <param name="ActiveAreaPerCell_cm2">Active electrode area per cell A [cm²].</param>
/// <param name="OperatingCurrentDensity_A_cm2">Nominal current density i [A/cm²].</param>
/// <param name="OperatingTemperature_C">Stack operating temperature T [°C].</param>
/// <param name="OperatingPressure_bar">Reactant inlet pressure P [bar].</param>
internal sealed record PemFuelCellDesign(
    PowerGenKind Kind,
    int    CellCount,
    double ActiveAreaPerCell_cm2,
    double OperatingCurrentDensity_A_cm2,
    double OperatingTemperature_C,
    double OperatingPressure_bar)
{
    /// <summary>Total active area summed across all cells [cm²].</summary>
    public double TotalActiveArea_cm2 => CellCount * ActiveAreaPerCell_cm2;

    /// <summary>Validate structural self-consistency of the design record.</summary>
    /// <exception cref="ArgumentException">When any dimension is non-positive.</exception>
    public void ValidateSelf()
    {
        if (Kind != PowerGenKind.PemFuelCell)
            throw new ArgumentException(
                $"Wave-1 supports only PemFuelCell; got {Kind}.", nameof(Kind));
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
