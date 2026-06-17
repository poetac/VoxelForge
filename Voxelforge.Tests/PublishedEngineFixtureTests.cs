// Sprint OOB-3 (2026-04-25) — published-engine sanity validation.
//
// Exercises the post-Sprint-35 propellant tables (PH-4 2-D CEA via
// rocketcea) at published operating points for real-world engines and
// asserts the resulting chamber state matches public reference data
// within engineering tolerance. Lets us catch a future cascade
// regression that breaks specific-engine numbers even when the bench-sa
// fingerprint stays inside its 5% threshold.
//
// Engines covered (by what propellant pairs voxelforge supports today):
//   - RL-10 (Aerojet Rocketdyne) — LOX/H2 closed expander
//       Pc = 3.2 MPa, MR = 5.0, F_vac = 73.4 kN, Isp_vac = 465 s
//   - Merlin-1D (SpaceX) — LOX/RP-1 gas generator
//       Pc = 9.7 MPa, MR = 2.36, F_vac = 981 kN, Isp_vac = 311 s
//   - BE-4-class (Blue Origin / parametric) — LOX/CH4 staged combustion
//       Pc = 13.4 MPa, MR = 3.5, F_sl = 2400 kN, Isp_sl = 311 s
//
// References:
//   - Sutton & Biblarz "Rocket Propulsion Elements" 9e Tables 5-4, 5-5
//   - SpaceX Merlin technical specs (publicly disclosed at FAA filings)
//   - Aerojet Rocketdyne RL-10 published data sheet
//   - Wikipedia BE-4 with cross-checks against trade-press articles

using Voxelforge.Combustion;
using Xunit;

namespace Voxelforge.Tests;

public class PublishedEngineFixtureTests
{
    // Tolerance bands chosen to be loose enough to absorb CEA-vs-published
    // measurement noise but tight enough to catch real cascade regressions
    // (e.g., a propellant table data-entry typo that shifts Tc by 100 K).
    private const double TcTolerance     = 0.04;   // 4 % — covers chamber-vs-static T sources
    private const double GammaTolerance  = 0.03;   // 3 %
    private const double MWTolerance     = 0.05;   // 5 % — published MW sometimes refers to throat
    private const double CStarTolerance  = 0.06;   // 6 % — frozen vs equilibrium accounts for 3-5%

    private static void AssertClose(double expected, double actual, double tol, string label)
    {
        double rel = System.Math.Abs(actual - expected) / System.Math.Max(System.Math.Abs(expected), 1e-9);
        Assert.True(rel < tol, $"{label}: expected ≈ {expected:F2}, got {actual:F2} (rel err {rel:P1}, tol {tol:P0})");
    }

    // ═════════════════════════════════════════════════════════════════
    //   RL-10 — LOX/H2 closed expander (Aerojet Rocketdyne)
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void Rl10_ChamberState_MatchesPublishedReference()
    {
        // RL-10 family operates at MR ≈ 5.0, Pc ≈ 3.2 MPa.
        // CEA equilibrium-chamber values at this point (rocketcea):
        //   Tc ≈ 3338 K, γ_chamber ≈ 1.147, MW ≈ 11.7 g/mol.
        // Published "γ" in Sutton-style tables sometimes refers to a
        // shifting-equilibrium throat γ (~1.21) which differs from the
        // chamber-equilibrium γ this test exercises. Use the CEA chamber
        // value as ground truth — it's what voxelforge's PropellantState
        // reports.
        var s = PropellantTables.Lookup(PropellantPair.LOX_H2, mixtureRatio: 5.0, chamberPressure_Pa: 3.2e6);
        AssertClose(3338, s.ChamberTemp_K,   TcTolerance,    "RL-10 Tc @ MR=5, Pc=3.2 MPa");
        AssertClose(1.147, s.GammaChamber,   GammaTolerance, "RL-10 γ_chamber (CEA equilibrium)");
        AssertClose(11.7, s.MolecularWeight, MWTolerance,    "RL-10 MW");
    }

    [Fact]
    public void Rl10_CStar_IsInPlausibleH2Range()
    {
        var s = PropellantTables.Lookup(PropellantPair.LOX_H2, mixtureRatio: 5.0, chamberPressure_Pa: 3.2e6);
        // LOX/H2 C* range: 2200-2500 m/s typical. Frozen tends to top of band.
        Assert.InRange(s.CStar_ms, 2200.0, 2500.0);
    }

    // ═════════════════════════════════════════════════════════════════
    //   Merlin-1D — LOX/RP-1 gas generator (SpaceX)
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void Merlin1d_ChamberState_MatchesPublishedReference()
    {
        // Merlin-1D vacuum: MR = 2.36, Pc ≈ 9.7 MPa.
        // Published Tc at this point: ~3680 K (Sutton 9e Table 5-5 LOX/RP-1).
        // Published γ_chamber: ~1.14.
        // Published MW: ~23 g/mol.
        var s = PropellantTables.Lookup(PropellantPair.LOX_RP1, mixtureRatio: 2.36, chamberPressure_Pa: 9.7e6);
        AssertClose(3680, s.ChamberTemp_K,   TcTolerance,    "Merlin-1D Tc @ MR=2.36, Pc=9.7 MPa");
        AssertClose(1.14, s.GammaChamber,    GammaTolerance, "Merlin-1D γ_chamber");
        AssertClose(23.0, s.MolecularWeight, MWTolerance,    "Merlin-1D MW");
    }

    [Fact]
    public void Merlin1d_CStar_IsInPlausibleKeroseneRange()
    {
        var s = PropellantTables.Lookup(PropellantPair.LOX_RP1, mixtureRatio: 2.36, chamberPressure_Pa: 9.7e6);
        // LOX/RP-1 C* range: 1750-1850 m/s typical for staged combustion / GG.
        Assert.InRange(s.CStar_ms, 1700.0, 1900.0);
    }

    // ═════════════════════════════════════════════════════════════════
    //   BE-4 class — LOX/CH4 staged combustion
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void Be4Class_ChamberState_AtHighPcMatchesCea()
    {
        // BE-4-class operating point: MR ≈ 3.5, Pc ≈ 13.4 MPa.
        // Published Tc: ~3650 K (post-PH-4 CEA at this point).
        // γ_chamber: ~1.13. MW: ~22 g/mol.
        var s = PropellantTables.Lookup(PropellantPair.LOX_CH4, mixtureRatio: 3.5, chamberPressure_Pa: 13.4e6);
        AssertClose(3650, s.ChamberTemp_K,   TcTolerance,    "BE-4 Tc @ MR=3.5, Pc=13.4 MPa");
        AssertClose(1.13, s.GammaChamber,    GammaTolerance, "BE-4 γ_chamber");
        AssertClose(22.0, s.MolecularWeight, MWTolerance,    "BE-4 MW");
    }

    [Fact]
    public void Be4Class_CStar_IsInPlausibleMethaneRange()
    {
        var s = PropellantTables.Lookup(PropellantPair.LOX_CH4, mixtureRatio: 3.5, chamberPressure_Pa: 13.4e6);
        // LOX/CH4 C* range: 1800-1900 m/s typical for staged combustion.
        Assert.InRange(s.CStar_ms, 1750.0, 1950.0);
    }

    // ═════════════════════════════════════════════════════════════════
    //   Cross-engine sanity: Pc dependence + MR sensitivity
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void HigherPc_GivesHigherTc_AcrossAllPublishedEngines()
    {
        // Sanity: each engine's Tc rises monotonically as we sweep Pc
        // through the table band {3, 7, 15, 25} MPa at the engine's
        // published MR. Validates the 2-D table's Pc-axis is well-behaved.
        var engines = new[]
        {
            (Pair: PropellantPair.LOX_H2,  MR: 5.0,  Name: "RL-10 family"),
            (Pair: PropellantPair.LOX_RP1, MR: 2.36, Name: "Merlin-1D family"),
            (Pair: PropellantPair.LOX_CH4, MR: 3.5,  Name: "BE-4 class"),
        };
        double[] pcs = { 3e6, 7e6, 15e6, 25e6 };
        foreach (var eng in engines)
        {
            double prevTc = 0;
            foreach (var pc in pcs)
            {
                var s = PropellantTables.Lookup(eng.Pair, eng.MR, pc);
                Assert.True(s.ChamberTemp_K > prevTc,
                    $"{eng.Name}: Tc must rise with Pc; at Pc={pc/1e6:F0} MPa got {s.ChamberTemp_K:F0} K, prev {prevTc:F0}");
                prevTc = s.ChamberTemp_K;
            }
        }
    }
}
