// PublishedEngineInjectorMassFluxTests — discipline tests pinning the
// G_INJ_TOO_HIGH / G_INJ_TOO_LOW gate thresholds against published
// injector mass flux numbers for real production engines.
//
// Why this exists: Sprint 36 (PH-21, 2026-04-24) shipped the gate with
// a bound of 140-500 kg/(m²·s), citing "Sutton §6.3 / Yang LPCI §5".
// That bound was 30-100× too low — every canonical bench-sa preset
// (merlin, rl10, aerospike, pintle, pressure-fed-small) tripped the
// G_INJ_TOO_HIGH gate on 100 % of evaluated candidates, blocking all
// SA progress. Investigation 2026-04-26 traced the bug to a likely
// units conversion error: Sutton 9e p. 270 quotes 7-60 lb/(in²·s),
// which converts to 4,925-42,200 kg/(m²·s) — not 140-500.
//
// These tests pin the corrected band against published-engine numbers
// so a future regression on the constants can't silently re-break
// every canonical preset. Numbers from public engine documentation:
//
//   • F-1 (Saturn V S-IC):  ṁ ≈ 2,580 kg/s, total inj area ≈ 0.158 m²
//                           → G_inj ≈ 16,300 kg/(m²·s)
//   • SSME (Block II):       ṁ ≈ 467 kg/s, total inj area ≈ 0.066 m²
//                           → G_inj ≈ 7,100 kg/(m²·s)
//   • Merlin-1D (SpaceX):   ṁ ≈ 277 kg/s, total inj area est. ~ 0.025 m²
//                           → G_inj ≈ 11,000 kg/(m²·s)
//
// All three sit comfortably inside the new 3,000-50,000 kg/(m²·s) band.
//
// References:
//   - Sutton & Biblarz, "Rocket Propulsion Elements" 9e, Chapter 8 p. 270
//   - F-1 engine: NASA SP-4206 "Stages to Saturn", Saturn V Flight Manual
//   - SSME injector: NASA RP-1311 / Pratt & Whitney Rocketdyne docs
//   - Merlin-1D: SpaceX FAA filings; engineers' technical reviews

using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests;

public class PublishedEngineInjectorMassFluxTests
{
    private const double F1_GInj_kgPm2s     = 16_300.0;
    private const double Ssme_GInj_kgPm2s   =  7_100.0;
    private const double Merlin_GInj_kgPm2s = 11_000.0;

    [Fact]
    public void F1_InjectorMassFlux_IsInsideStableBand()
    {
        Assert.InRange(F1_GInj_kgPm2s,
            FeasibilityGate.InjectorMassFluxFloor_kgPerm2s,
            FeasibilityGate.InjectorMassFluxCeiling_kgPerm2s);
    }

    [Fact]
    public void Ssme_InjectorMassFlux_IsInsideStableBand()
    {
        Assert.InRange(Ssme_GInj_kgPm2s,
            FeasibilityGate.InjectorMassFluxFloor_kgPerm2s,
            FeasibilityGate.InjectorMassFluxCeiling_kgPerm2s);
    }

    [Fact]
    public void Merlin1d_InjectorMassFlux_IsInsideStableBand()
    {
        Assert.InRange(Merlin_GInj_kgPm2s,
            FeasibilityGate.InjectorMassFluxFloor_kgPerm2s,
            FeasibilityGate.InjectorMassFluxCeiling_kgPerm2s);
    }

    [Fact]
    public void StableBand_BracketsSuttonChapter8Range()
    {
        // Sutton 9e p. 270 quotes 7-60 lb/(in²·s) = 4,925-42,200 kg/(m²·s)
        // for "low-impulse, low-Pc" through "high-Pc regen-cooled" engines.
        // Floor must be ≤ Sutton's lower bound; ceiling ≥ Sutton's upper bound.
        const double SuttonLow_kgPm2s  =  4_925.0;
        const double SuttonHigh_kgPm2s = 42_200.0;
        Assert.True(FeasibilityGate.InjectorMassFluxFloor_kgPerm2s <= SuttonLow_kgPm2s,
            $"Floor {FeasibilityGate.InjectorMassFluxFloor_kgPerm2s} > Sutton low {SuttonLow_kgPm2s}");
        Assert.True(FeasibilityGate.InjectorMassFluxCeiling_kgPerm2s >= SuttonHigh_kgPm2s,
            $"Ceiling {FeasibilityGate.InjectorMassFluxCeiling_kgPerm2s} < Sutton high {SuttonHigh_kgPm2s}");
    }

    [Fact]
    public void StableBand_DoesNotPermitGratuitouslyLowOrHighValues()
    {
        // Sanity bounds — protect against future re-bumps that go too far.
        Assert.True(FeasibilityGate.InjectorMassFluxFloor_kgPerm2s >= 1_000.0,
            "Floor below 1,000 kg/(m²·s) would mask genuine chug-instability candidates.");
        Assert.True(FeasibilityGate.InjectorMassFluxCeiling_kgPerm2s <= 100_000.0,
            "Ceiling above 100,000 kg/(m²·s) would mask genuine face-burnout candidates.");
    }
}
