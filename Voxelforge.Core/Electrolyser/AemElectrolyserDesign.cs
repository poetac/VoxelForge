// AemElectrolyserDesign.cs — Sprint EL.W2 AEM electrolyser stack
// design.
//
// Sized to bracket Enapter EL-2.1 / Hydrolite-class commercial AEM
// electrolysers (~ 2-5 kW stacks running ~ 30-60 cells at ~ 50-100
// cm² active area, 50-70 °C, 1-35 bar). The pillar mirrors PEM
// in shape — they're the same physics topology (Nernst + Tafel +
// ohmic) but with different membrane resistance + kinetics anchors.
//
// Parallel-class pattern (not polymorphic): matches the EP Wave-2
// solver-per-kind convention (HET / Arcjet / PPT / GIT each have
// their own *PlasmaState + *CycleSolver classes). If a third
// electrolyser kind (SOEC or Alkaline) arrives, refactor to a shared
// abstraction per ADR-029a rule-of-three pattern.

using System;

namespace Voxelforge.Electrolyser;

/// <summary>
/// Design parameters for a single AEM electrolyser stack (Sprint
/// EL.W2 scaffold). Standalone — does not integrate with the
/// IEngine&lt;,,&gt; stack yet (deferred to a future EL.W3 sprint when
/// a shared electrolyser abstraction lands).
/// </summary>
/// <param name="Kind">Sub-variant — <see cref="ElectrolyserKind.Aem"/> for Wave-2.</param>
/// <param name="CellCount">Number of series-stacked cells N [-].</param>
/// <param name="ActiveAreaPerCell_cm2">Active electrode area per cell A [cm²].</param>
/// <param name="OperatingCurrentDensity_A_cm2">Nominal current density i [A/cm²].</param>
/// <param name="OperatingTemperature_C">Stack operating temperature T [°C].</param>
/// <param name="OperatingPressure_bar">Stack operating pressure P [bar].</param>
internal sealed record AemElectrolyserDesign(
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
        if (Kind != ElectrolyserKind.Aem)
            throw new ArgumentException(
                $"AemElectrolyserDesign supports only Aem; got {Kind}.", nameof(Kind));
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
                "OperatingTemperature_C must be > 0 (Celsius — AEM is liquid-water-balanced).",
                nameof(OperatingTemperature_C));
        if (OperatingPressure_bar <= 0)
            throw new ArgumentException("OperatingPressure_bar must be > 0.",
                nameof(OperatingPressure_bar));
    }
}
