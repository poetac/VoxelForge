// PizzarelliAutoSelectTests.cs — coverage for the per-station Pizzarelli
// auto-selection in CoolantCorrelations.AutoSelectKind (A3, 2026-04-28).
//
// The auto-select promotes Sieder-Tate / Dittus-Boelter to Pizzarelli at
// stations where the bulk state lies inside the fluid's pseudocritical
// transition band. This file pins:
//   1. Far-from-T_pc bulk state → user's default kind unchanged.
//   2. Near-T_pc bulk state → SupercriticalPizzarelli, regardless of default.
//   3. Null fluid → user's kind unchanged (back-compat for synthetic callers).
//   4. User explicitly asked for Pizzarelli → kept (never downgraded).

using Voxelforge.Coolant;
using Voxelforge.HeatTransfer;

namespace Voxelforge.Tests;

public class PizzarelliAutoSelectTests
{
    // Build a CoolantState for a given (T, P) using the actual fluid module's
    // GetState. That guarantees the test agrees with the production code path.
    private static CoolantState State(ICoolantFluid fluid, double T_K, double P_Pa)
        => fluid.GetState(T_K, P_Pa);

    [Fact]
    public void AutoSelect_NullFluid_PreservesUserKind()
    {
        // Synthetic call site without a fluid — must return user's kind
        // unchanged. Pre-A3 callers passed only the user's kind down to
        // CoolantCorrelations.HeatTransferCoefficient; null-fluid is the
        // back-compat marker for those paths.
        var dummy = new CoolantState(150, 10e6, 420, 3500, 1e-4, 0.18, 1.2, 0);
        var kind = CoolantCorrelations.AutoSelectKind(
            in dummy, fluid: null, userKind: CoolantCorrelationKind.SiederTate);
        Assert.Equal(CoolantCorrelationKind.SiederTate, kind);
    }

    [Fact]
    public void AutoSelect_UserPickedPizzarelli_NeverDowngrades()
    {
        // User explicitly asked for SupercriticalPizzarelli — auto-select
        // must never downgrade to a less-conservative correlation.
        var ch4 = MethaneFluid.Instance;
        var farFromTpc = State(ch4, 130, 10e6);   // sub-critical liquid CH4
        var kind = CoolantCorrelations.AutoSelectKind(
            in farFromTpc, ch4, CoolantCorrelationKind.SupercriticalPizzarelli);
        Assert.Equal(CoolantCorrelationKind.SupercriticalPizzarelli, kind);
    }

    [Fact]
    public void AutoSelect_LH2_FarFromTpc_KeepsUserDefault()
    {
        // LH2 critical T = 33.2 K, P_crit = 1.3 MPa. At 50 K + 8 MPa we're
        // well above T_pc and well above P_crit (typical regen jacket inlet
        // for an expander cycle): fluid.IsInPseudocriticalRegion = false
        // (per HydrogenFluid implementation: only inside the actual band).
        var lh2 = HydrogenFluid.Instance;
        var farState = State(lh2, 50, 8e6);
        var kind = CoolantCorrelations.AutoSelectKind(
            in farState, lh2, CoolantCorrelationKind.SiederTate);
        Assert.Equal(CoolantCorrelationKind.SiederTate, kind);
    }

    [Fact]
    public void AutoSelect_LH2_InPseudocriticalBand_PromotesToPizzarelli()
    {
        // LH2 in the pseudocritical transition band — auto-select promotes
        // even when the user's default is Sieder-Tate. The exact T_pc for
        // LH2 at supercritical pressure depends on the fluid's tabulation;
        // the assertion below uses the fluid's IsInPseudocriticalRegion as
        // the source of truth (avoids hardcoding T_pc here).
        var lh2 = HydrogenFluid.Instance;
        // Sweep T at 8 MPa to find a T inside the band.
        //
        // Integer-tick sweep (#553 audit C3): the original `for (double T =
        // 25; T <= 100; T += 1.0)` is FP-accumulator-prone; reconstruct T
        // from the tick index so the closed [25, 100] endpoint is always
        // visited regardless of rounding drift.
        double tInBand = -1;
        int nSteps = (int)System.Math.Round((100.0 - 25.0) / 1.0) + 1;  // closed [25, 100]
        for (int i = 0; i < nSteps; i++)
        {
            double T = 25.0 + i * 1.0;
            if (lh2.IsInPseudocriticalRegion(T, 8e6)) { tInBand = T; break; }
        }
        Assert.True(tInBand > 0,
            "Could not find an LH2 pseudocritical-band T at 8 MPa "
          + "in [25, 100] K — review HydrogenFluid.IsInPseudocriticalRegion.");

        var bandState = State(lh2, tInBand, 8e6);
        var kind = CoolantCorrelations.AutoSelectKind(
            in bandState, lh2, CoolantCorrelationKind.SiederTate);
        Assert.Equal(CoolantCorrelationKind.SupercriticalPizzarelli, kind);
    }

    [Fact]
    public void AutoSelect_Methane_FarFromTpc_KeepsUserDefault()
    {
        // CH4 sub-critical (190 K crit, 4.6 MPa crit). At 130 K + 6 MPa
        // we're below T_pc — IsInPseudocriticalRegion = false.
        var ch4 = MethaneFluid.Instance;
        var farState = State(ch4, 130, 6e6);
        var kind = CoolantCorrelations.AutoSelectKind(
            in farState, ch4, CoolantCorrelationKind.DittusBoelter);
        Assert.Equal(CoolantCorrelationKind.DittusBoelter, kind);
    }
}
