// AlkalineElectrolyserDesign.cs — Sprint B.2-Alk alkaline electrolyser
// stack design.
//
// Sized to bracket Nel A485 / Thyssenkrupp / Asahi-Kasei / Hydrogenics
// HyLYZER-class commercial alkaline electrolysers (kW-to-MW stacks,
// ~ 100-500 cells, ~ 500-10 000 cm² active area, 60-90 °C, 1-30 bar).
// The pillar mirrors PEM/AEM in shape — they share the same physics
// topology (Nernst + Tafel + ohmic) — but anchors different Tafel +
// resistance parameters to match the Ni-catalyst / diaphragm-separator
// architecture.
//
// Parallel-class pattern (not polymorphic): matches the EP Wave-2
// solver-per-kind convention. With three electrolyser kinds shipped
// (PEM, AEM, Alkaline), the rule-of-three refactor to a shared
// abstraction (per ADR-029a) becomes a worthwhile follow-on once SOEC
// arrives — SOEC requires fundamentally different physics (high-T
// thermo + ionic O²⁻ conduction in YSZ) and would change the
// abstraction shape, so deferring the refactor until then is correct.

using System;

namespace Voxelforge.Electrolyser;

/// <summary>
/// Design parameters for a single alkaline electrolyser stack (Sprint
/// B.2-Alk scaffold). Standalone — does not integrate with the
/// IEngine&lt;,,&gt; stack yet (deferred to a future EL.W3 sprint when
/// a shared electrolyser abstraction lands).
/// </summary>
/// <param name="Kind">Sub-variant — <see cref="ElectrolyserKind.Alkaline"/> for B.2-Alk.</param>
/// <param name="CellCount">Number of series-stacked cells N [-].</param>
/// <param name="ActiveAreaPerCell_cm2">Active electrode area per cell A [cm²].</param>
/// <param name="OperatingCurrentDensity_A_cm2">Nominal current density i [A/cm²].</param>
/// <param name="OperatingTemperature_C">Stack operating temperature T [°C].</param>
/// <param name="OperatingPressure_bar">Stack operating pressure P [bar].</param>
internal sealed record AlkalineElectrolyserDesign(
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
        if (Kind != ElectrolyserKind.Alkaline)
            throw new ArgumentException(
                $"AlkalineElectrolyserDesign supports only Alkaline; got {Kind}.",
                nameof(Kind));
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
                "OperatingTemperature_C must be > 0 (Celsius — alkaline runs liquid-water + KOH).",
                nameof(OperatingTemperature_C));
        if (OperatingPressure_bar <= 0)
            throw new ArgumentException("OperatingPressure_bar must be > 0.",
                nameof(OperatingPressure_bar));
    }
}
