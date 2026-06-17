// Ph27OrscMrRealismTests.cs — closes physics-correctness item PH-27.
//
// Pre-2026-04-30 the ORSC ox-rich preburner MR defaults were pedagogical
// (LOX/RP-1 = 25, LOX/CH4 = 35) — under-estimating real-engine values
// to keep the synthetic T_c output away from the
// ORSC_PREBURNER_OXCORROSION gate threshold. Audit finding: the
// underlying PropellantTables extrapolation into MR > 8 is unreliable
// regardless of which value you pick, so hiding the uncertainty behind
// conservative MR is worse than disclosing it openly.
//
// PH-27 ships:
// (a) MR defaults tightened to literature: LOX/RP-1 = 58 (RD-180),
//     LOX/CH4 = 60 (Raptor estimate), LOX/H2 unchanged at 150
//     (theoretical placeholder).
// (b) PreburnerResult.Notes carries a [PH-27 disclosure: ...] tag when
//     MR > OrscTableExtrapolationMrThreshold (= 8) so consumers can
//     attach an uncertainty caveat to the warm-gas T_c output.
//
// These tests pin both the literature-aligned defaults AND the Notes
// flag behaviour. Two failure modes to catch:
// (1) MR drifts back to pedagogical defaults (regression).
// (2) Notes flag silently disappears (e.g. if SizeFfscDual stops
//     stamping the Notes string).

using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.FeedSystem;

namespace Voxelforge.Tests;

public class Ph27OrscMrRealismTests
{
    // ─────────────────────────────────────────────────────────────────
    //  Default MR values match literature
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Ph27_OrscMr_LoxRp1_AlignsWithRD180()
    {
        // RD-180 / RD-191 (NPO Energomash) is the only flight ox-rich
        // kerosene cycle. Published spec puts the ORP MR ≈ 58 with
        // turbine inlet ~770 K. Pre-PH-27 voxelforge returned 25.
        double mr = PreburnerChamber.SuggestOxRichPreburnerMr(PropellantPair.LOX_RP1);
        Assert.Equal(58.0, mr, precision: 1);
        Assert.True(mr > PreburnerChamber.OrscTableExtrapolationMrThreshold,
            "LOX/RP-1 ORSC MR must trigger PH-27 table-extrapolation disclosure.");
    }

    [Fact]
    public void Ph27_OrscMr_LoxCh4_AlignsWithRaptorEstimate()
    {
        // SpaceX has not published Raptor's ORP MR exactly; public
        // flow-balance estimates put it in the ~55-65 range. We pick
        // 60 as a representative midpoint. Pre-PH-27 voxelforge
        // returned 35 (pedagogical, low).
        double mr = PreburnerChamber.SuggestOxRichPreburnerMr(PropellantPair.LOX_CH4);
        Assert.Equal(60.0, mr, precision: 1);
        Assert.True(mr > PreburnerChamber.OrscTableExtrapolationMrThreshold,
            "LOX/CH4 ORSC MR must trigger PH-27 table-extrapolation disclosure.");
    }

    [Fact]
    public void Ph27_OrscMr_LoxH2_RetainsTheoreticalPlaceholder()
    {
        // No flight LOX/H2 ORSC engine exists. Value retained at 150
        // as a theoretical placeholder; the dilution requirement is
        // huge because LH2 combustion is already very tolerant.
        double mr = PreburnerChamber.SuggestOxRichPreburnerMr(PropellantPair.LOX_H2);
        Assert.Equal(150.0, mr, precision: 1);
    }

    [Fact]
    public void Ph27_OrscMr_AllPairsExceedTableExtrapolationThreshold()
    {
        // The PH-27 disclosure pattern only adds value if it actually
        // fires for every flight-realistic ORSC pair. If a future MR
        // tightening drops a pair below the threshold, disclosure
        // would become silent — flag that here.
        foreach (var pair in new[]
        {
            PropellantPair.LOX_CH4,
            PropellantPair.LOX_H2,
            PropellantPair.LOX_RP1,
        })
        {
            double mr = PreburnerChamber.SuggestOxRichPreburnerMr(pair);
            Assert.True(mr > PreburnerChamber.OrscTableExtrapolationMrThreshold,
                $"{pair} ORSC MR ({mr}) ≤ extrapolation threshold "
              + $"({PreburnerChamber.OrscTableExtrapolationMrThreshold}) — "
              + $"PH-27 disclosure would not fire on this design.");
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  PreburnerResult.Notes carries the PH-27 disclosure tag
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Ph27_FfscDual_OxRichPreburner_NotesContainsExtrapolationDisclosure()
    {
        // SizeFfscDual stamps the Notes field with the PH-27 disclosure
        // when MR > OrscTableExtrapolationMrThreshold. Use the default
        // MRs (which now exceed the threshold by construction) so this
        // test validates the disclosure mechanism end-to-end.
        const double M_fuel = 1.0;            // kg/s
        const double overallMr = 3.6;         // typical LOX/CH4 main-chamber MR
        double M_ox = M_fuel * overallMr;
        var (fr, or) = PreburnerChamber.SizeFfscDual(
            pair:                  PropellantPair.LOX_CH4,
            fuelRichMr:            0,         // use default fuel-rich MR
            oxRichMr:              0,         // use default ORSC MR (60)
            preburnerPc_Pa:        20e6,
            totalFuelMassFlow_kgs: M_fuel,
            totalOxMassFlow_kgs:   M_ox);

        // Fuel-rich side: low MR, no disclosure.
        Assert.DoesNotContain("PH-27 disclosure", fr.Notes);

        // Ox-rich side: MR=60 > threshold=8, so disclosure must appear.
        Assert.Contains("PH-27 disclosure", or.Notes);
        Assert.Contains("MR > 8 extrapolates PropellantTables", or.Notes);
        Assert.Contains("Validate against direct CEA", or.Notes);
    }

    [Fact]
    public void Ph27_FfscDual_OxRichPreburner_LowMrSuppressesDisclosure()
    {
        // If a user explicitly overrides the ox-rich MR to a value
        // inside the table calibration band (e.g. for an experimental
        // design exploring lower-MR ORSC), the disclosure should NOT
        // fire — the underlying PropellantTables result is reliable in
        // that regime. Pin this so the disclosure stays calibration-aware.
        const double M_fuel = 1.0;
        const double overallMr = 3.6;
        double M_ox = M_fuel * overallMr;
        var (_, or) = PreburnerChamber.SizeFfscDual(
            pair:                  PropellantPair.LOX_CH4,
            fuelRichMr:            0,
            oxRichMr:              7.0,       // below threshold → no disclosure
            preburnerPc_Pa:        20e6,
            totalFuelMassFlow_kgs: M_fuel,
            totalOxMassFlow_kgs:   M_ox);

        Assert.DoesNotContain("PH-27 disclosure", or.Notes);
        // Sanity: regular ox-rich tagging still present.
        Assert.Contains("FFSC ox-rich preburner", or.Notes);
    }
}
