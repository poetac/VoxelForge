// ElectrolyserSolverTests — pins all four electrolyser sub-variants
// (EL.W1 PEM, EL.W2 AEM, B.2-Alk Alkaline, B.2-SOEC) on the shared
// loss decomposition V_cell = E_Nernst + η_act(Tafel) + η_ohm(i·R_AS),
// the per-kind anchors (R_AS 0.15/0.30/0.25/0.40 Ω·cm², Tafel
// 60/60/90/100 mV/dec, i₀ 1e-7 vs SOEC 0.5 A/cm²), the SOEC
// steam-electrolysis Nernst reference (0.923 V @ 800 °C, −0.234 mV/K),
// Faraday's-law H₂ production ṁ = N·I·M/(2F), and η_HHV = 1.481/V_cell
// (> 1 for SOEC — endothermic heat absorption, correct and physical).
// Every expected value hand-derived and independently recomputed
// (python3, mirroring the solver's operation order) before assertion.
// Backfills coverage onto the Linux 'core' CI leg (ROADMAP → Now §2).

using System;
using Voxelforge.Electrolyser;
using Xunit;

namespace Voxelforge.Core.Tests;

public sealed class ElectrolyserSolverTests
{
    // Nel/ITM-class PEM stack: 200 cells × 200 cm², 1.5 A/cm², 70 °C, 10 bar.
    private static PemElectrolyserDesign PemStack()
        => new(ElectrolyserKind.Pem, CellCount: 200, ActiveAreaPerCell_cm2: 200.0,
               OperatingCurrentDensity_A_cm2: 1.5,
               OperatingTemperature_C: 70.0, OperatingPressure_bar: 10.0);

    // Enapter-class AEM stack: 100 cells × 100 cm², 1.0 A/cm², 60 °C, 10 bar.
    private static AemElectrolyserDesign AemStack()
        => new(ElectrolyserKind.Aem, CellCount: 100, ActiveAreaPerCell_cm2: 100.0,
               OperatingCurrentDensity_A_cm2: 1.0,
               OperatingTemperature_C: 60.0, OperatingPressure_bar: 10.0);

    // Nel-A485-class alkaline stack: 150 cells × 300 cm², 0.3 A/cm², 80 °C, 1 bar.
    private static AlkalineElectrolyserDesign AlkalineStack()
        => new(ElectrolyserKind.Alkaline, CellCount: 150, ActiveAreaPerCell_cm2: 300.0,
               OperatingCurrentDensity_A_cm2: 0.3,
               OperatingTemperature_C: 80.0, OperatingPressure_bar: 1.0);

    // Sunfire-HyLink-class SOEC stack: 100 cells × 100 cm², 0.7 A/cm²,
    // 800 °C (the Nernst reference point exactly), 1 bar.
    private static SoecElectrolyserDesign SoecStack()
        => new(ElectrolyserKind.Soec, CellCount: 100, ActiveAreaPerCell_cm2: 100.0,
               OperatingCurrentDensity_A_cm2: 0.7,
               OperatingTemperature_C: 800.0, OperatingPressure_bar: 1.0);

    [Fact]
    public void Pem_Anchor_ReproducesVoltageBreakdownAndFaradayRate()
    {
        var r = PemElectrolyserSolver.Solve(PemStack());
        // E_Nernst = 1.229 − 0.85e-3·(343.15−298.15) + (RT/2F)·ln 10.
        Assert.Equal(1.224794263509795, r.NernstVoltage_V, 12);
        // η_act = (0.060/ln10)·ln(1.5/1e-7); η_ohm = 1.5·0.15.
        Assert.Equal(0.43056547554334085, r.ActivationLoss_V, 12);
        Assert.Equal(0.225, r.OhmicLoss_V, 12);
        Assert.Equal(1.880359739053136, r.CellVoltage_V, 12);
        Assert.Equal(376.0719478106272, r.StackVoltage_V, 9);
        Assert.Equal(300.0, r.StackCurrent_A, 9);
        Assert.Equal(112821.58434318815, r.StackElectricPower_W, 6);
        Assert.Equal(0.7876152468281228, r.HhvEfficiency, 12);
        // ṁ = 200·300·2.016e-3/(2·96485) — Faraday's law, exact.
        Assert.Equal(0.0006268331865056744, r.HydrogenProductionRate_kgs, 15);
        Assert.Equal(25.106925719023682, r.HydrogenProductionRate_Nm3_h, 9);
    }

    [Fact]
    public void Aem_Anchor_DiffersFromPemOnlyThroughItsHigherRas()
    {
        var r = AemElectrolyserSolver.Solve(AemStack());
        Assert.Equal(1.232302153251605, r.NernstVoltage_V, 12);
        // i/i₀ = 1e7 → η_act = (0.060/ln10)·ln(1e7) = 0.060·7 = 0.42.
        Assert.Equal(0.42, r.ActivationLoss_V, 12);
        // The AEM differentiator: η_ohm = 1.0·0.30 (PEM would give 0.15).
        Assert.Equal(0.30, r.OhmicLoss_V, 12);
        Assert.Equal(1.952302153251605, r.CellVoltage_V, 12);
        Assert.Equal(19523.02153251605, r.StackElectricPower_W, 6);
        Assert.Equal(0.7585915927682403, r.HhvEfficiency, 12);
        Assert.Equal(0.00010447219775094574, r.HydrogenProductionRate_kgs, 15);
    }

    [Fact]
    public void Alkaline_Anchor_CarriesTheNiTafelSlopePenalty()
    {
        var r = AlkalineElectrolyserSolver.Solve(AlkalineStack());
        // P = 1 bar → ln term vanishes: E = 1.229 − 0.85e-3·55 = 1.18225.
        Assert.Equal(1.18225, r.NernstVoltage_V, 12);
        // 90 mV/dec Ni-OER slope: η_act = (0.090/ln10)·ln(0.3/1e-7).
        Assert.Equal(0.5829409129247696, r.ActivationLoss_V, 12);
        Assert.Equal(0.075, r.OhmicLoss_V, 12);        // 0.3·0.25
        Assert.Equal(1.8401909129247696, r.CellVoltage_V, 12);
        Assert.Equal(24842.577324484388, r.StackElectricPower_W, 6);
        Assert.Equal(0.8048077998853514, r.HhvEfficiency, 12);
        Assert.Equal(0.00014103746696377675, r.HydrogenProductionRate_kgs, 15);
        Assert.Equal(5.6490582867803285, r.HydrogenProductionRate_Nm3_h, 9);
    }

    [Fact]
    public void Soec_Anchor_RunsBelowThermoNeutralWithEtaHhvAboveOne()
    {
        var r = SoecElectrolyserSolver.Solve(SoecStack());
        // At exactly 800 °C / 1 bar both correction terms vanish:
        // E_Nernst = the 0.923 V steam-electrolysis reference.
        Assert.Equal(0.923, r.NernstVoltage_V, 12);
        // Facile 800 °C kinetics: η_act = (0.100/ln10)·ln(0.7/0.5) — small.
        Assert.Equal(0.0146128035678238, r.ActivationLoss_V, 12);
        Assert.Equal(0.28, r.OhmicLoss_V, 12);          // 0.7·0.40
        Assert.Equal(1.2176128035678238, r.CellVoltage_V, 12);
        // V_cell < 1.481 V thermo-neutral → η_HHV > 1 on electric input:
        // the SOEC absorbs heat to complete the endothermic reaction.
        Assert.True(r.CellVoltage_V < SoecElectrolyserSolver.HhvThermoNeutralVoltage_V);
        Assert.Equal(1.216314411001925, r.HhvEfficiency, 12);
        Assert.Equal(8523.289624974766, r.StackElectricPower_W, 6);
        Assert.Equal(7.313053842566202e-05, r.HydrogenProductionRate_kgs, 15);
        Assert.Equal(2.929141333886096, r.HydrogenProductionRate_Nm3_h, 9);
    }

    [Fact]
    public void AllKinds_CellVoltageExceedsNernst_ElectrolysisSignConvention()
    {
        // The electrolyser consumes power: V_cell > E_Nernst always
        // (opposite of the fuel-cell sign convention).
        var pem = PemElectrolyserSolver.Solve(PemStack());
        var aem = AemElectrolyserSolver.Solve(AemStack());
        var alk = AlkalineElectrolyserSolver.Solve(AlkalineStack());
        var soec = SoecElectrolyserSolver.Solve(SoecStack());
        Assert.True(pem.CellVoltage_V > pem.NernstVoltage_V);
        Assert.True(aem.CellVoltage_V > aem.NernstVoltage_V);
        Assert.True(alk.CellVoltage_V > alk.NernstVoltage_V);
        Assert.True(soec.CellVoltage_V > soec.NernstVoltage_V);
    }

    [Fact]
    public void FaradayRate_DependsOnlyOnChargeThroughput_NotKind()
    {
        // Same N·I ⇒ same ṁ_H₂ regardless of electrolyser chemistry:
        // 2 e⁻ per H₂ molecule is charge-carrier-independent.
        var pem = PemElectrolyserSolver.Solve(PemStack() with
        { CellCount = 100, ActiveAreaPerCell_cm2 = 100.0, OperatingCurrentDensity_A_cm2 = 1.0 });
        var aem = AemElectrolyserSolver.Solve(AemStack() with
        { OperatingCurrentDensity_A_cm2 = 1.0 });
        var soec = SoecElectrolyserSolver.Solve(SoecStack() with
        { OperatingCurrentDensity_A_cm2 = 1.0 });
        Assert.Equal(pem.HydrogenProductionRate_kgs, aem.HydrogenProductionRate_kgs, 15);
        Assert.Equal(pem.HydrogenProductionRate_kgs, soec.HydrogenProductionRate_kgs, 15);
    }

    [Fact]
    public void Designs_RejectMismatchedKindsAndInvalidFields()
    {
        Assert.Throws<ArgumentException>(() =>
            PemElectrolyserSolver.Solve(PemStack() with { Kind = ElectrolyserKind.Aem }));
        Assert.Throws<ArgumentException>(() =>
            AemElectrolyserSolver.Solve(AemStack() with { Kind = ElectrolyserKind.Pem }));
        Assert.Throws<ArgumentException>(() =>
            AlkalineElectrolyserSolver.Solve(AlkalineStack() with { Kind = ElectrolyserKind.Soec }));
        Assert.Throws<ArgumentException>(() =>
            SoecElectrolyserSolver.Solve(SoecStack() with { Kind = ElectrolyserKind.Alkaline }));
        Assert.Throws<ArgumentException>(() =>
            PemElectrolyserSolver.Solve(PemStack() with { CellCount = 0 }));
        Assert.Throws<ArgumentException>(() =>
            AemElectrolyserSolver.Solve(AemStack() with { OperatingCurrentDensity_A_cm2 = 0.0 }));
        Assert.Throws<ArgumentException>(() =>
            SoecElectrolyserSolver.Solve(SoecStack() with { OperatingPressure_bar = -1.0 }));
    }
}
