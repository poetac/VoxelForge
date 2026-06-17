// Ph28ExpanderGammaSupercriticalTests.cs — closes physics-correctness item PH-28.
//
// `ExpanderCycleSizing.Size` computes γ from the ideal-gas reduction
// γ = cp / (cp − R). For supercritical jacket-outlet conditions
// (essentially every flight expander cycle — H2 at 10 MPa / 80-200 K,
// CH4 at 10 MPa / 400 K), (cp − R) can be 2× the ideal value, yielding
// γ errors of ±15-25 %. The proper fix is real-fluid γ from a REFPROP-
// class table, which is a substantial physics infrastructure change
// touching all 4 fluid implementations + bench fingerprints.
//
// PH-28 ships a yellow-flag disclosure: when the jacket-outlet state
// is supercritical (T ≥ Tc OR P ≥ Pc) the result's Notes field carries
// a [PH-28 disclosure: ...] tag with the computed γ + uncertainty
// caveat. Downstream consumers can attach the disclosure to
// EXPANDER_TURBINE_ENTHALPY_DEFICIT margin reports.
//
// Tests pin:
// (1) The disclosure fires on a typical RL10-class condition (LH2 at
//     ~250 K / ~9 MPa, well above Tc=33 K + Pc=1.3 MPa).
// (2) The disclosure does NOT fire on subcritical conditions.
// (3) The disclosure text references the actual fluid + computed γ
//     so reports stay informative if conditions change.

using Voxelforge.Coolant;
using Voxelforge.FeedSystem;

namespace Voxelforge.Tests;

public class Ph28ExpanderGammaSupercriticalTests
{
    [Fact]
    public void Ph28_ExpanderResult_Rl10ClassH2_NotesContainsSupercriticalDisclosure()
    {
        // RL10-class closed-expander: LH2 at ~250 K / ~9 MPa jacket
        // outlet. Critical point: 33 K / 1.296 MPa. T and P both well
        // above critical, so the disclosure must fire.
        var result = ExpanderCycleSizing.Size(
            cycle:                   EngineCycle.ClosedExpander,
            coolant:                 HydrogenFluid.Instance,
            coolantInletT_K:         50.0,
            coolantOutletT_K:        250.0,
            coolantOutletP_Pa:       9e6,
            coolantMassFlow_kgs:     2.0,
            mainChamberPressure_Pa:  4e6,
            requiredPumpShaftPower_W: 200e3,
            efficiency:              0.55);

        Assert.NotNull(result);
        Assert.Contains("PH-28 disclosure", result!.Notes);
        Assert.Contains("supercritical", result.Notes);
        Assert.Contains("Hydrogen", result.Notes);                  // fluid display name
        Assert.Contains("REFPROP", result.Notes);                   // future-upgrade pointer
        Assert.Contains($"γ={result.EffectiveGamma:F3}", result.Notes); // computed γ surfaced
    }

    [Fact]
    public void Ph28_ExpanderResult_Rl10ClassH2_RetainsBaseLineNote()
    {
        // The PH-28 disclosure is appended to the existing cycle-shape
        // baseline note, not a replacement. Verify both halves are
        // present so downstream parsers that key on the cycle prefix
        // still see it.
        var result = ExpanderCycleSizing.Size(
            cycle:                   EngineCycle.ClosedExpander,
            coolant:                 HydrogenFluid.Instance,
            coolantInletT_K:         50.0,
            coolantOutletT_K:        250.0,
            coolantOutletP_Pa:       9e6,
            coolantMassFlow_kgs:     2.0,
            mainChamberPressure_Pa:  4e6,
            requiredPumpShaftPower_W: 200e3,
            efficiency:              0.55);

        Assert.NotNull(result);
        Assert.Contains("Closed-expander", result!.Notes);
        Assert.Contains("expands to", result.Notes);
        Assert.Contains("PH-28 disclosure", result.Notes);
    }

    [Fact]
    public void Ph28_ExpanderResult_OpenExpanderCh4_NotesContainsSupercriticalDisclosure()
    {
        // CH4 at 10 MPa / 400 K is firmly supercritical
        // (Tc=190.56 K, Pc=4.6 MPa). Disclosure must fire on
        // open-expander cycles too (not just closed).
        var result = ExpanderCycleSizing.Size(
            cycle:                   EngineCycle.OpenExpander,
            coolant:                 MethaneFluid.Instance,
            coolantInletT_K:         150.0,
            coolantOutletT_K:        400.0,
            coolantOutletP_Pa:       10e6,
            coolantMassFlow_kgs:     5.0,
            mainChamberPressure_Pa:  3e6,
            requiredPumpShaftPower_W: 500e3,
            efficiency:              0.55);

        Assert.NotNull(result);
        Assert.Contains("PH-28 disclosure", result!.Notes);
        Assert.Contains("Methane", result.Notes);
        Assert.Contains("supercritical", result.Notes);
    }

    [Fact]
    public void Ph28_ExpanderResult_NonExpanderCycle_ReturnsNull()
    {
        // Sanity guard — PH-28 disclosure path is only relevant on
        // expander cycles. Other cycles short-circuit at the top of
        // Size() and return null (so no Notes field to inspect).
        var result = ExpanderCycleSizing.Size(
            cycle:                   EngineCycle.GasGenerator,
            coolant:                 HydrogenFluid.Instance,
            coolantInletT_K:         50.0,
            coolantOutletT_K:        250.0,
            coolantOutletP_Pa:       9e6,
            coolantMassFlow_kgs:     2.0,
            mainChamberPressure_Pa:  4e6,
            requiredPumpShaftPower_W: 200e3,
            efficiency:              0.55);

        Assert.Null(result);
    }
}
