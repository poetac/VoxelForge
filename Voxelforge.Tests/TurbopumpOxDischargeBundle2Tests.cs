// TurbopumpOxDischargeBundle2Tests — discipline tests pinning the
// separate ox-pump-discharge behavior added in physics-integrity-
// bundle-2 (2026-04-27, ID-3).
//
// Why this exists: pre-bundle-2 `TurbopumpSizing.Size` used a single
// shared `dischargePressure_Pa` for both fuel and ox pumps. Real
// engines have substantially different fuel and ox pump discharges:
//
//   • RL10:   fuel ~14 MPa, ox ~5 MPa, ratio 2.8×
//   • Merlin: fuel ~15 MPa, ox ~12 MPa, ratio 1.25×
//   • F-1:    fuel ~9 MPa, ox ~7 MPa, ratio 1.3×
//
// The shared-discharge bug was particularly visible on expander cycles
// where Sprint F1 (PR #88) bumped fuel-pump discharge to 5× Pc to
// satisfy turbine pressure ratio. That over-spec'd the OX pump shaft
// power on expanders by 4-5×. Bundle-2 routes ox pump correctly to
// `Pc × 1.2` (chamber pressure plus typical 20 % injector ΔP) by
// default while preserving back-compat: callers who don't pass
// `oxDischargePressure_Pa` still get the shared-discharge legacy path.

using Voxelforge.Coolant;
using Voxelforge.FeedSystem;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests;

public class TurbopumpOxDischargeBundle2Tests
{
    private static OperatingConditions MakeCond(EngineCycle cycle = EngineCycle.GasGenerator)
        => new OperatingConditions
        {
            Thrust_N = 100_000,
            ChamberPressure_Pa = 7e6,
            MixtureRatio = 2.5,
            CoolantInletTemp_K = 120,
            CoolantInletPressure_Pa = 8e6,
            EngineCycle = cycle,
            PropellantPair = Combustion.PropellantPair.LOX_CH4,
            PumpInletPressure_Pa = 1.5e6,
            WallMaterialIndex = 4,
            PumpEfficiency = 0.65,
        };

    [Fact]
    public void DefaultOxDischarge_PreservesLegacyBehavior()
    {
        // Calling without oxDischargePressure_Pa should produce the same
        // per-pump shaft power as before bundle-2 — both pumps use the
        // shared dischargePressure_Pa.
        var cond = MakeCond();
        var legacy = TurbopumpSizing.Size(
            cycle: EngineCycle.GasGenerator,
            cond: cond,
            fuelFlow_kgs: 8.0,
            oxFlow_kgs: 20.0,
            fuelDensity_kgm3: 425,
            oxDensity_kgm3: 1140,
            fuelInletPressure_Pa: 1.5e6,
            oxInletPressure_Pa: 1.5e6,
            dischargePressure_Pa: 14e6);

        // No explicit oxDischargePressure → legacy back-compat path.
        Assert.NotNull(legacy.FuelPump);
        Assert.NotNull(legacy.OxPump);
        // Both pumps see the same head rise → same dischargePressure_Pa.
        Assert.Equal(legacy.FuelPump!.DischargePressure_Pa,
                     legacy.OxPump!.DischargePressure_Pa,
                     precision: 0);
    }

    [Fact]
    public void ExplicitOxDischarge_ProducesLowerOxShaftPower()
    {
        // When ox pump is sized for chamber+injector ΔP only (typical
        // 1.2 × Pc), it requires substantially less shaft power than
        // the shared 14 MPa expander-cycle fuel discharge.
        var cond = MakeCond();
        double Pc = cond.ChamberPressure_Pa;
        var withSeparate = TurbopumpSizing.Size(
            cycle: EngineCycle.GasGenerator,
            cond: cond,
            fuelFlow_kgs: 8.0,
            oxFlow_kgs: 20.0,
            fuelDensity_kgm3: 425,
            oxDensity_kgm3: 1140,
            fuelInletPressure_Pa: 1.5e6,
            oxInletPressure_Pa: 1.5e6,
            dischargePressure_Pa: 14e6,
            oxDischargePressure_Pa: Pc * 1.2);

        var legacy = TurbopumpSizing.Size(
            cycle: EngineCycle.GasGenerator,
            cond: cond,
            fuelFlow_kgs: 8.0,
            oxFlow_kgs: 20.0,
            fuelDensity_kgm3: 425,
            oxDensity_kgm3: 1140,
            fuelInletPressure_Pa: 1.5e6,
            oxInletPressure_Pa: 1.5e6,
            dischargePressure_Pa: 14e6);

        // OX pump shaft power should drop substantially (was 14 MPa
        // discharge, now 8.4 MPa — ~50 % drop in head rise).
        Assert.True(withSeparate.OxPump!.ShaftPower_W
                    < legacy.OxPump!.ShaftPower_W * 0.7,
            $"Expected separate ox-discharge to drop ox shaft power ≥ 30 %, "
          + $"got {withSeparate.OxPump.ShaftPower_W:F0} vs "
          + $"{legacy.OxPump.ShaftPower_W:F0}.");

        // Issue #274: post-PH-48 (PR #269), common-shaft enforcement
        // couples the fuel pump to the constraining shaft RPM. When the
        // ox-pump's lower discharge pressure shifts its N_s-derived RPM,
        // the shared shaft RPM follows. Fuel hydraulic power is unchanged
        // (same Q, same H), but η shifts as the operating N_s slides off
        // peak — so fuel ShaftPower_W = P_hyd / η changes with RPM. The
        // pre-PH-48 strict-equality assertion was relaxed during the
        // merge to a wide order-of-magnitude check; replace it now with
        // a tight proportional-coupling invariant that documents the
        // new physics.
        Assert.True(withSeparate.FuelPump!.ShaftPower_W > 0);

        double rpmRel   = System.Math.Abs(legacy.FuelPump!.Rpm - withSeparate.FuelPump.Rpm)
                        / legacy.FuelPump.Rpm;
        double powerRel = System.Math.Abs(legacy.FuelPump.ShaftPower_W - withSeparate.FuelPump.ShaftPower_W)
                        / legacy.FuelPump.ShaftPower_W;

        // Fuel RPM shift must exceed the 0.5 % gate threshold — anything
        // smaller would have left COMMON_SHAFT_RPM_INCONSISTENT silent in
        // the legacy run, which contradicts the hypothesis that ox
        // discharge perturbs the constraining shaft RPM.
        Assert.True(rpmRel >= 0.005,
            $"Expected fuel RPM to shift on common-shaft coupling; "
          + $"got rel diff {rpmRel:P3} "
          + $"(legacy {legacy.FuelPump.Rpm:F0}, withSeparate {withSeparate.FuelPump.Rpm:F0}).");

        // Power and RPM move together. Hydraulic power is fixed (same Q,
        // same H), so |ΔP/P| = |Δη/η|; the Stepanoff η-vs-N_s curve is
        // monotonic on each side of the 2700 peak, so |Δη/η| stays inside
        // a few times |ΔRPM/RPM|. Empirically this ratio lands ~0.5 on
        // the configured Merlin-class GG inputs. Bounds [0.1, 5.0] allow
        // ~10× margin for future numerical drift while still flagging a
        // regression where the coupling silently disappears (ratio → 0)
        // or runs away (ratio → ∞).
        double powerToRpmRatio = powerRel / rpmRel;
        Assert.InRange(powerToRpmRatio, 0.1, 5.0);
    }

    [Fact]
    public void ExplicitOxDischarge_RecordsCorrectDischargeOnOxPump()
    {
        var cond = MakeCond();
        double Pc = cond.ChamberPressure_Pa;
        double oxDischarge = Pc * 1.2;
        var sized = TurbopumpSizing.Size(
            cycle: EngineCycle.GasGenerator,
            cond: cond,
            fuelFlow_kgs: 8.0,
            oxFlow_kgs: 20.0,
            fuelDensity_kgm3: 425,
            oxDensity_kgm3: 1140,
            fuelInletPressure_Pa: 1.5e6,
            oxInletPressure_Pa: 1.5e6,
            dischargePressure_Pa: 14e6,
            oxDischargePressure_Pa: oxDischarge);

        Assert.Equal(oxDischarge, sized.OxPump!.DischargePressure_Pa, precision: 0);
        Assert.Equal(14e6, sized.FuelPump!.DischargePressure_Pa, precision: 0);
    }

    [Fact]
    public void ExpanderCycle_OxPumpNotOverSpecdByFuelDischarge()
    {
        // The Sprint F1 bug was that closed-expander cycles bumped the
        // SHARED discharge to 5× Pc to satisfy turbine PR. Pre-bundle-2
        // this over-spec'd OX pump shaft power 4-5×; bundle-2 routes
        // OX pump to 1.2× Pc independent of expander pressure pressure
        // requirements. Verify the OX shaft power is sensible (matches
        // the 1.2× Pc discharge size, not the 5× Pc fuel size).
        var cond = MakeCond(EngineCycle.ClosedExpander);
        cond = cond with { ChamberPressure_Pa = 4e6 };  // RL10 class
        double fuelDischarge = 4e6 * 5.0;  // 20 MPa expander fuel
        double oxDischarge = 4e6 * 1.2;    // 4.8 MPa ox

        var sized = TurbopumpSizing.Size(
            cycle: EngineCycle.ClosedExpander,
            cond: cond,
            fuelFlow_kgs: 8.0,
            oxFlow_kgs: 32.0,                 // RL10 ox flow
            fuelDensity_kgm3: 70,             // LH2
            oxDensity_kgm3: 1140,             // LOX
            fuelInletPressure_Pa: 1.5e6,
            oxInletPressure_Pa: 1.5e6,
            dischargePressure_Pa: fuelDischarge,
            oxDischargePressure_Pa: oxDischarge);

        // OX pump head rise should reflect 4.8 MPa minus 1.5 MPa inlet
        // = 3.3 MPa → ~300 m head rise at LOX density 1140 kg/m³.
        // Legacy (shared) would have head ≈ (20-1.5) MPa / (1140 × 9.81)
        // = 1655 m → 5.5× higher.
        Assert.True(sized.OxPump!.HeadRise_m < 500,
            $"Expected ox head rise < 500 m post-bundle-2, got "
          + $"{sized.OxPump.HeadRise_m:F0} m.");
    }
}
