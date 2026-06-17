// PropellantTableTests.cs — Lock the CEA tables in place.
//
// These are characterization (golden) tests: if someone edits a table and
// materially shifts the chamber temperature or C*, a test fails with the
// delta so it's easy to see whether the change was intentional.
//
// Tolerances are ±1 % on T_c and ±2 % on C* — tighter than the CEA-vs-fire
// uncertainty but loose enough that small re-curves through the table
// points don't break the suite.

using Voxelforge.Combustion;

namespace Voxelforge.Tests;

public class PropellantTableTests
{
    private const double Tc_Tolerance = 0.01;      // 1 %
    private const double CStar_Tolerance = 0.02;   // 2 %
    private const double Gamma_Tolerance = 0.02;
    private const double MW_Tolerance = 0.02;

    private static void AssertClose(double expected, double actual, double tol, string label)
    {
        double rel = Math.Abs(actual - expected) / Math.Max(Math.Abs(expected), 1e-9);
        Assert.True(rel < tol, $"{label}: expected {expected:F1}, got {actual:F1} (rel err {rel:P1})");
    }

    [Fact]
    public void LoxMethane_AtPeakCStar_MatchesCEA()
    {
        // PH-4 (Sprint 35): values from rocketcea CEA equilibrium chamber
        // at Pc=7 MPa, MR=3.25. Pre-PH-4 hand-tuned table had Tc=3535 K;
        // CEA gives 3531. Within 1 % — matches.
        var s = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.25, 7.0e6);
        AssertClose(3531, s.ChamberTemp_K, Tc_Tolerance, "LOX/CH4 Tc @ MR=3.25");
        AssertClose(1.133, s.Gamma, Gamma_Tolerance, "LOX/CH4 γ @ MR=3.25");
        AssertClose(21.25, s.MolecularWeight, MW_Tolerance, "LOX/CH4 MW @ MR=3.25");
        // C* recomputed from (R, Tc, Γ(γ)). For LOX/CH4 MR=3.25 Pc=7 MPa
        // CEA equilibrium chamber: ~1840 m/s. ±3 % envelope.
        AssertClose(1840, s.CStar_ms, 0.03, "LOX/CH4 C* @ MR=3.25");
    }

    [Fact]
    public void LoxMethane_Pc_DependenceIsBounded()
    {
        // PH-4 (Sprint 35): replaces the pre-PH-4 "γ should decrease with Pc"
        // assumption with an empirical bounds-only test. Real CEA shows the
        // direction is MR-dependent (at fuel-rich MR=3.3 γ rises slightly
        // with Pc due to differing CO/CO2/H2/H2O ratios), contradicting the
        // textbook "more dissociation at low Pc → higher γ" narrative which
        // only holds when atomic species dominate.
        var sLow  = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.3, 3.0e6);   // table low end
        var sHigh = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.3, 25.0e6);  // table high end
        // T_c rises with P_c (suppressed dissociation reduces enthalpy
        // absorbed by the CO/H2/OH endothermic decomposition).
        Assert.True(sHigh.ChamberTemp_K > sLow.ChamberTemp_K,
            $"Tc should increase with Pc: low={sLow.ChamberTemp_K:F0} high={sHigh.ChamberTemp_K:F0}");
        // Effect on Tc bounded — cap at 15 % either direction across the
        // 3-25 MPa span. Real CEA at MR=3.3: ~6-8 % across the band.
        double TcRel = Math.Abs(sHigh.ChamberTemp_K - sLow.ChamberTemp_K) / sLow.ChamberTemp_K;
        Assert.True(TcRel < 0.15, $"Pc correction too large on Tc: {TcRel:P1}");
        // γ stays bounded too — < 5 % change across 3-25 MPa.
        double gammaRel = Math.Abs(sHigh.Gamma - sLow.Gamma) / sLow.Gamma;
        Assert.True(gammaRel < 0.05, $"Pc effect on γ unrealistically large: {gammaRel:P1}");
    }

    [Fact]
    public void LoxHydrogen_AtPeakCStar_MatchesCEA()
    {
        // PH-4 (Sprint 35): pre-PH-4 hand-tuned table gave Tc=3280 K at
        // MR=4, Pc=7 MPa. CEA equilibrium chamber gives 3090 K — the
        // ~6 % difference is the hand-tuned table's error at peak-C*
        // (Tc was over-stated; γ was over-stated symmetrically). PH-4
        // brings these in line with rocketcea.
        var s = PropellantTables.Lookup(PropellantPair.LOX_H2, 4.0, 7.0e6);
        AssertClose(3090, s.ChamberTemp_K, Tc_Tolerance, "LOX/H2 Tc @ MR=4");
        AssertClose(1.179, s.Gamma, Gamma_Tolerance, "LOX/H2 γ @ MR=4");
        AssertClose(9.96, s.MolecularWeight, MW_Tolerance, "LOX/H2 MW @ MR=4");
        // C* = √(R·Tc)/Γ(γ) → R = 8314.5 / 9.96 ≈ 835 J/(kg·K);
        // Γ(1.179) ≈ 0.635 → C* ≈ 2530 m/s. Pre-PH-4 hand-tuned C*
        // was ~2515. ±5 % envelope.
        AssertClose(2530, s.CStar_ms, 0.05, "LOX/H2 C* @ MR=4");
    }

    [Fact]
    public void LoxRP1_AtStoichiometric_MatchesCEA()
    {
        // PH-4 (Sprint 35): values within 1-2 % of pre-PH-4 hand-tuned;
        // RP-1 was the closest-fit pair pre-PH-4 (the kerosene chamber
        // has less dissociation than methane/H2 so simpler tabulation
        // worked better).
        var s = PropellantTables.Lookup(PropellantPair.LOX_RP1, 2.56, 7.0e6);
        AssertClose(3672, s.ChamberTemp_K, Tc_Tolerance, "LOX/RP1 Tc @ MR=2.56");
        AssertClose(1.138, s.Gamma, Gamma_Tolerance, "LOX/RP1 γ @ MR=2.56");
        AssertClose(23.35, s.MolecularWeight, MW_Tolerance, "LOX/RP1 MW @ MR=2.56");
        AssertClose(1790, s.CStar_ms, 0.03, "LOX/RP1 C* @ MR=2.56");
    }

    // ═════════════════════════════════════════════════════════════════
    //   PH-4 (Sprint 35): 2-D bilinear table coverage tests
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void PressureDependence_LOX_CH4_TcIncreasesWithPc()
    {
        // Across all 4 Pc anchors at fixed MR=3.3, Tc must rise monotonically.
        double[] pcs = { 3e6, 7e6, 15e6, 25e6 };
        double prevTc = 0;
        foreach (var pc in pcs)
        {
            var s = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.3, pc);
            Assert.True(s.ChamberTemp_K > prevTc,
                $"Tc should increase with Pc: at Pc={pc/1e6} MPa got Tc={s.ChamberTemp_K:F0}, prev={prevTc:F0}");
            prevTc = s.ChamberTemp_K;
        }
    }

    [Fact]
    public void PressureDependence_LOX_H2_TcIncreasesWithPc()
    {
        double[] pcs = { 3e6, 7e6, 15e6, 25e6 };
        double prevTc = 0;
        foreach (var pc in pcs)
        {
            var s = PropellantTables.Lookup(PropellantPair.LOX_H2, 5.0, pc);
            Assert.True(s.ChamberTemp_K > prevTc,
                $"LOX/H2 Tc should increase with Pc: at Pc={pc/1e6} MPa got Tc={s.ChamberTemp_K:F0}, prev={prevTc:F0}");
            prevTc = s.ChamberTemp_K;
        }
    }

    [Fact]
    public void PressureDependence_LOX_RP1_TcIncreasesWithPc()
    {
        double[] pcs = { 3e6, 7e6, 15e6, 25e6 };
        double prevTc = 0;
        foreach (var pc in pcs)
        {
            var s = PropellantTables.Lookup(PropellantPair.LOX_RP1, 2.5, pc);
            Assert.True(s.ChamberTemp_K > prevTc,
                $"LOX/RP1 Tc should increase with Pc: at Pc={pc/1e6} MPa got Tc={s.ChamberTemp_K:F0}, prev={prevTc:F0}");
            prevTc = s.ChamberTemp_K;
        }
    }

    [Fact]
    public void BilinearSmoothing_LOX_CH4_IntermediatePoint()
    {
        // Lookup at intermediate (MR=3.375 between 3.25 and 3.5; Pc=11 MPa
        // between 7 and 15) must lie within the four surrounding corners.
        var corner_lo_lo = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.25,  7e6);
        var corner_lo_hi = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.25, 15e6);
        var corner_hi_lo = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.5,   7e6);
        var corner_hi_hi = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.5,  15e6);
        var interior     = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.375, 11e6);

        double minTc = Math.Min(Math.Min(corner_lo_lo.ChamberTemp_K, corner_lo_hi.ChamberTemp_K),
                                Math.Min(corner_hi_lo.ChamberTemp_K, corner_hi_hi.ChamberTemp_K));
        double maxTc = Math.Max(Math.Max(corner_lo_lo.ChamberTemp_K, corner_lo_hi.ChamberTemp_K),
                                Math.Max(corner_hi_lo.ChamberTemp_K, corner_hi_hi.ChamberTemp_K));
        Assert.InRange(interior.ChamberTemp_K, minTc, maxTc);
    }

    [Fact]
    public void ClampingBelowPcGrid_ReturnsFirstRow()
    {
        // Pc=1 MPa is below the 3 MPa anchor — must clamp to Pc=3 MPa.
        var sBelow = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.3, 1.0e6);
        var sFirst = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.3, 3.0e6);
        Assert.Equal(sFirst.ChamberTemp_K, sBelow.ChamberTemp_K);
        Assert.Equal(sFirst.GammaChamber,  sBelow.GammaChamber);
    }

    [Fact]
    public void ClampingAbovePcGrid_ReturnsLastRow()
    {
        // Pc=30 MPa is above the 25 MPa anchor — must clamp to Pc=25 MPa.
        var sAbove = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.3, 30.0e6);
        var sLast  = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.3, 25.0e6);
        Assert.Equal(sLast.ChamberTemp_K, sAbove.ChamberTemp_K);
        Assert.Equal(sLast.GammaChamber,  sAbove.GammaChamber);
    }

    [Fact]
    public void AllImplementedPairsAreLookupable()
    {
        foreach (var meta in PropellantPairs.All)
        {
            if (!meta.Implemented) continue;
            var s = PropellantTables.Lookup(meta.Id, meta.MR_Default, 7.0e6);
            Assert.True(s.ChamberTemp_K > 2000 && s.ChamberTemp_K < 4500,
                $"{meta.Name}: Tc out of plausible range ({s.ChamberTemp_K:F0} K)");
            Assert.True(s.Gamma > 1.10 && s.Gamma < 1.35,
                $"{meta.Name}: γ out of plausible range ({s.Gamma:F3})");
            Assert.True(s.CStar_ms > 1500 && s.CStar_ms < 2600,
                $"{meta.Name}: C* out of plausible range ({s.CStar_ms:F0} m/s)");
        }
    }

    [Fact]
    public void UnimplementedPairsThrow()
    {
        // PropellantNotImplementedException is a sealed
        // subtype of NotImplementedException so UI/CLI callers can catch
        // it specifically. xUnit Assert.Throws<T> matches the exact type,
        // so we assert the specific type here.
        Assert.Throws<PropellantNotImplementedException>(() =>
            PropellantTables.Lookup(PropellantPair.N2O4_MMH, 1.85, 7.0e6));
    }

    [Fact]
    public void IsentropicFlow_AreaRatio_ConvergesBothRoots()
    {
        // At γ=1.4, A/A*=5 has two Mach roots: subsonic ≈ 0.115, supersonic ≈ 3.17.
        // (γ=1.4 chosen because it's the textbook standard; γ=1.15 gives
        // Msup ≈ 2.70 but references vary, so we pin the textbook value.)
        double Msub  = PropellantTables.MachFromAreaRatio(5.0, 1.4, supersonic: false);
        double Msup  = PropellantTables.MachFromAreaRatio(5.0, 1.4, supersonic: true);
        Assert.InRange(Msub, 0.10, 0.13);
        Assert.InRange(Msup, 3.0, 3.3);
    }

    [Fact]
    public void AdiabaticWallTemp_IsAbove_StaticTemp()
    {
        double Tstatic = PropellantTables.StaticTemp(3500, 2.5, 1.15);
        double Taw = PropellantTables.AdiabaticWallTemp(Tstatic, 2.5, 1.15, 0.55);
        Assert.True(Taw > Tstatic, "recovery T must exceed static T for compressible flow");
        Assert.True(Taw < 3500, "recovery T must stay below stagnation for non-zero M");
    }
}
