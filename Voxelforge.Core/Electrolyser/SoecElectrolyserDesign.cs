// SoecElectrolyserDesign.cs — Sprint B.2-SOEC solid-oxide electrolyser
// stack design.
//
// Sized to bracket Sunfire HyLink / Topsoe HTSE / Ceres Power class
// commercial high-temperature SOEC stacks (kW-to-MW modules, ~ 100-800
// cells, ~ 80-200 cm² active area, 700-850 °C, atmospheric to modest
// pressure). The pillar shares the Tafel + ohmic loss shape with PEM /
// AEM / Alkaline but anchors a DIFFERENT Nernst formulation — at SOEC's
// operating temperature the reactant is steam (vapour) not liquid water,
// and the linear -0.85 mV/K slope used by the three low-T kinds diverges
// from the cluster above ~ 150 °C because it implicitly tracks the
// liquid-water heat-capacity reference.
//
// Parallel-class pattern (not polymorphic): matches the existing PEM /
// AEM / Alkaline shape. With SOEC arriving, the rule-of-three refactor
// to a shared electrolyser abstraction (per ADR-029a, flagged in the
// AlkalineElectrolyserDesign.cs header) becomes the next worthwhile
// follow-on — deferred to a separate issue so this sprint stays scoped
// to the parallel-class addition.

using System;

namespace Voxelforge.Electrolyser;

/// <summary>
/// Design parameters for a single SOEC (solid-oxide electrolyser cell)
/// stack (Sprint B.2-SOEC scaffold). Standalone — does not integrate
/// with the IEngine&lt;,,&gt; stack yet (deferred to a future EL.W3 sprint
/// when a shared electrolyser abstraction lands).
/// </summary>
/// <param name="Kind">Sub-variant — <see cref="ElectrolyserKind.Soec"/> for B.2-SOEC.</param>
/// <param name="CellCount">Number of series-stacked cells N [-].</param>
/// <param name="ActiveAreaPerCell_cm2">Active electrode area per cell A [cm²].</param>
/// <param name="OperatingCurrentDensity_A_cm2">Nominal current density i [A/cm²].</param>
/// <param name="OperatingTemperature_C">Stack operating temperature T [°C]. SOEC operates 600-900 °C; cluster mid-band 800 °C.</param>
/// <param name="OperatingPressure_bar">Stack operating pressure P [bar]. SOEC typically atmospheric to ~ 5 bar.</param>
internal sealed record SoecElectrolyserDesign(
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
        if (Kind != ElectrolyserKind.Soec)
            throw new ArgumentException(
                $"SoecElectrolyserDesign supports only Soec; got {Kind}.", nameof(Kind));
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
                "OperatingTemperature_C must be > 0 (Celsius — SOEC is steam-balanced; "
              + "cluster band 600-900 °C).",
                nameof(OperatingTemperature_C));
        if (OperatingPressure_bar <= 0)
            throw new ArgumentException("OperatingPressure_bar must be > 0.",
                nameof(OperatingPressure_bar));
    }
}
