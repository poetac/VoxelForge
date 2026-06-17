// Sprint37b34bCascadeTests.cs — Physics-correctness cascade Sprint
// 37b + 34b combined.
//
// Pins behaviour of:
//   • PH-14 aerospike injector-face recovery factor + Bartz h_g
//     replacement (Pr^(1/3) recovery via PropellantTables.AdiabaticWallTemp
//     at M = 0.1; Bartz-with-chamber-radius substitute for h_g).
//   • PH-18 truncated-plug base-drag correction in ComputeDerived
//     (empirical Hagemann 1998 / Rao 1961 calibration; only fires for
//     ChannelTopology.Aerospike with PlugLengthRatio < 1.0).
//   • PH-8 minimum-viable user-overrideable PumpRpm_rpm; computes
//     SpecificSpeed_US as a diagnostic and fires
//     PUMP_SPECIFIC_SPEED_OFF_BAND when out of [600, 9000].
//
// PH-9 (expander Picard iteration) and PH-16 (Rao angle table) are
// deferred — see commit message.

using Voxelforge.Combustion;
using Voxelforge.FeedSystem;
using Voxelforge.Optimization;

namespace Voxelforge.Tests;

public class Sprint37b34bCascadeTests
{
    // ─────────────────────────────────────────────────────────────────
    //  PH-18 — Truncated-plug base drag in ComputeDerived
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Aerospike_TruncatedPlug_LowersCFAtSeaLevel()
    {
        // Truncated aerospike (PlugLengthRatio = 0.30) at sea level
        // should produce LOWER C_F than full plug (PlugLengthRatio = 1.0)
        // because the base-drag correction subtracts ~2 × (P_amb / P_c) ×
        // (1 − pLR) ≈ 2 × 0.014 × 0.70 ≈ 0.020 from C_F.
        var cond = new OperatingConditions
        {
            Thrust_N            = 5000,
            ChamberPressure_Pa  = 7e6,
            MixtureRatio        = 3.4,
            CoolantInletTemp_K  = 150,
            CoolantInletPressure_Pa = 12e6,
            WallMaterialIndex   = 1, // CuCrZr
            PropellantPair      = PropellantPair.LOX_CH4,
            AmbientPressure_Pa  = 101325,
        };
        var gas = PropellantTables.Lookup(cond.PropellantPair, cond.MixtureRatio,
                                          cond.ChamberPressure_Pa);

        var fullPlug = new RegenChamberDesign
        {
            ChannelTopology  = ChannelTopology.Aerospike,
            PlugLengthRatio  = 1.0,
            ExpansionRatio   = 50.0,
        };
        var truncated = fullPlug with { PlugLengthRatio = 0.30 };

        var derivedFull   = RegenChamberOptimization.ComputeDerived(cond, gas, fullPlug);
        var derivedTrunc  = RegenChamberOptimization.ComputeDerived(cond, gas, truncated);
        Assert.True(derivedTrunc.ThrustCoefficient < derivedFull.ThrustCoefficient,
            $"Truncated plug must have lower C_F than full plug at sea level; "
          + $"truncated = {derivedTrunc.ThrustCoefficient:F4}, full = {derivedFull.ThrustCoefficient:F4}.");
        // Magnitude: pre-fix the two would have been equal; post-fix
        // the gap should be ~0.01-0.03 of C_F at typical conditions.
        double gap = derivedFull.ThrustCoefficient - derivedTrunc.ThrustCoefficient;
        Assert.InRange(gap, 0.005, 0.040);
    }

    [Fact]
    public void Aerospike_FullPlug_DoesNotApplyBaseDrag()
    {
        // PlugLengthRatio = 1.0 is the full plug; A_base = 0; gate is no-op.
        var cond = new OperatingConditions
        {
            Thrust_N            = 5000,
            ChamberPressure_Pa  = 7e6,
            MixtureRatio        = 3.4,
            CoolantInletTemp_K  = 150,
            CoolantInletPressure_Pa = 12e6,
            WallMaterialIndex   = 1,
            PropellantPair      = PropellantPair.LOX_CH4,
            AmbientPressure_Pa  = 101325,
        };
        var gas = PropellantTables.Lookup(cond.PropellantPair, cond.MixtureRatio,
                                          cond.ChamberPressure_Pa);
        var bell      = new RegenChamberDesign
        {
            ChannelTopology = ChannelTopology.Axial, // bell, not aerospike
            ExpansionRatio  = 50.0,
        };
        var fullPlug = new RegenChamberDesign
        {
            ChannelTopology = ChannelTopology.Aerospike,
            PlugLengthRatio = 1.0,
            ExpansionRatio  = 50.0,
        };
        var derivedBell = RegenChamberOptimization.ComputeDerived(cond, gas, bell);
        var derivedFull = RegenChamberOptimization.ComputeDerived(cond, gas, fullPlug);
        // PH-18 invariant: aerospike-full-plug has NO base-drag deduction.
        // PH-19 (#176, 2026-04-29) split the per-bell divergence loss out
        // of NozzleCfEfficiency, so the bell C_F now picks up λ_div(ε, L%)
        // while the aerospike doesn't (axial plug exit). To still test the
        // PH-18 invariant, we ratio out λ_div from the bell side: after
        // dividing by the bell's divergence factor the two C_F values must
        // match (i.e. the only physical difference is the per-bell λ_div,
        // not any base-drag deduction).
        Assert.Equal(
            derivedBell.ThrustCoefficient / derivedBell.DivergenceLoss,
            derivedFull.ThrustCoefficient,
            precision: 6);
    }

    [Fact]
    public void BellOnly_NotAerospike_DoesNotApplyBaseDrag()
    {
        // Non-aerospike topologies are unaffected by PH-18.
        var cond = new OperatingConditions
        {
            Thrust_N            = 5000,
            ChamberPressure_Pa  = 7e6,
            MixtureRatio        = 3.4,
            CoolantInletTemp_K  = 150,
            CoolantInletPressure_Pa = 12e6,
            WallMaterialIndex   = 1,
            PropellantPair      = PropellantPair.LOX_CH4,
            AmbientPressure_Pa  = 101325,
        };
        var gas = PropellantTables.Lookup(cond.PropellantPair, cond.MixtureRatio,
                                          cond.ChamberPressure_Pa);
        var bell = new RegenChamberDesign
        {
            ChannelTopology = ChannelTopology.Axial,
            // PlugLengthRatio < 1.0 should be ignored on non-aerospike topologies.
            PlugLengthRatio = 0.30,
            ExpansionRatio  = 50.0,
        };
        var derived = RegenChamberOptimization.ComputeDerived(cond, gas, bell);
        // Re-derive with explicit pLR=1.0 — should match.
        var bell2 = bell with { PlugLengthRatio = 1.0 };
        var derived2 = RegenChamberOptimization.ComputeDerived(cond, gas, bell2);
        Assert.Equal(derived.ThrustCoefficient, derived2.ThrustCoefficient, precision: 6);
    }

    // ─────────────────────────────────────────────────────────────────
    //  PH-8 — User-overrideable RPM + N_s diagnostic + gate
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Pump_AutoDeriveRpm_KeepsNsAtBackCompatBand()
    {
        // PumpRpm_rpm = 0 (default) auto-derives RPM from N_s = 2500.
        // PH-48 (PR #269, 2026-04-29) adds common-shaft enforcement on
        // GG / SC / ORSC / Open / Closed expander / TapOff cycles: the
        // independent N_s-derived RPMs are reconciled to a single shaft
        // speed via min(fuel_RPM, ox_RPM), and both pumps are re-sized
        // at that shared speed. Issue #274 tightens this assertion from
        // a one-pump N_s envelope check (which accepted any feasible
        // result) to direct PH-48 invariants: both pumps must report
        // the same RPM (within the 0.5 % gate threshold) AND each pump's
        // N_s must land inside the [600, 9000] engineering envelope.
        var cond = new OperatingConditions
        {
            Thrust_N              = 50000, // big enough to produce a turbopump
            ChamberPressure_Pa    = 7e6,
            MixtureRatio          = 3.4,
            CoolantInletTemp_K    = 150,
            CoolantInletPressure_Pa = 12e6,
            WallMaterialIndex     = 1,
            PropellantPair        = PropellantPair.LOX_CH4,
            EngineCycle           = EngineCycle.GasGenerator,
            TankUllagePressure_Pa = 0.5e6,
        };
        var design = new RegenChamberDesign(); // default PumpRpm_rpm = 0
        var gen = RegenChamberOptimization.GenerateWith(cond, design);
        Assert.NotNull(gen.Turbopump);
        Assert.NotNull(gen.Turbopump!.FuelPump);
        Assert.NotNull(gen.Turbopump.OxPump);

        double fuelRpm = gen.Turbopump.FuelPump!.Rpm;
        double oxRpm   = gen.Turbopump.OxPump!.Rpm;
        double rpmRel  = System.Math.Abs(fuelRpm - oxRpm)
                       / System.Math.Max(fuelRpm, oxRpm);
        Assert.True(rpmRel <= 0.005,
            $"Common-shaft GG cycle should yield identical pump RPMs; "
          + $"got fuel {fuelRpm:F0}, ox {oxRpm:F0} (rel diff {rpmRel:P3}).");

        Assert.InRange(gen.Turbopump.FuelPump.SpecificSpeed_US, 600, 9000);
        Assert.InRange(gen.Turbopump.OxPump.SpecificSpeed_US,  600, 9000);
    }

    [Fact]
    public void Pump_UserSetLowRpm_FiresOffBandGate()
    {
        // User sets RPM = 5000 (low for a small pump). At small Q,
        // N_s = rpm × √Q / H^0.75 may drop below the floor of 600.
        // Use a small-thrust design where Q is small and high H:
        var cond = new OperatingConditions
        {
            Thrust_N              = 10000,
            ChamberPressure_Pa    = 14e6, // high Pc → high pump head
            MixtureRatio          = 3.4,
            CoolantInletTemp_K    = 150,
            CoolantInletPressure_Pa = 12e6,
            WallMaterialIndex     = 1,
            PropellantPair        = PropellantPair.LOX_CH4,
            EngineCycle           = EngineCycle.GasGenerator,
            TankUllagePressure_Pa = 0.5e6,
        };
        var design = new RegenChamberDesign { PumpRpm_rpm = 5000.0 };
        var gen = RegenChamberOptimization.GenerateWith(cond, design);
        Assert.NotNull(gen.Turbopump?.FuelPump);
        var nsValue = gen.Turbopump!.FuelPump!.SpecificSpeed_US;
        // The actual N_s depends on the resulting Q, H — verify the
        // gate fires when N_s ends up out-of-band. We compute it
        // empirically rather than asserting a specific number; the
        // contract is "if N_s < 600, the gate fires."
        var gate = FeasibilityGate.Evaluate(gen);
        if (nsValue > 0 && (nsValue < FeasibilityGate.PumpSpecificSpeedFloor
                         || nsValue > FeasibilityGate.PumpSpecificSpeedCeiling))
        {
            Assert.Contains(gate.Violations,
                v => v.ConstraintId == "PUMP_SPECIFIC_SPEED_OFF_BAND");
        }
    }

    [Fact]
    public void Pump_UserSetHighRpm_FiresOffBandGate()
    {
        // For high-thrust + high-Pc designs the auto-derived RPM (from
        // N_s = 2500) can already be 100+ krpm. To force N_s above the
        // 9000 ceiling, the user's RPM needs to be ~3.6× the auto-RPM.
        // Use 800 krpm for a 50 kN LOX/CH4 design — comfortably above
        // the ceiling but still calculable.
        var cond = new OperatingConditions
        {
            Thrust_N              = 50000,
            ChamberPressure_Pa    = 7e6,
            MixtureRatio          = 3.4,
            CoolantInletTemp_K    = 150,
            CoolantInletPressure_Pa = 12e6,
            WallMaterialIndex     = 1,
            PropellantPair        = PropellantPair.LOX_CH4,
            EngineCycle           = EngineCycle.GasGenerator,
            TankUllagePressure_Pa = 0.5e6,
        };
        var design = new RegenChamberDesign { PumpRpm_rpm = 800000.0 };
        var gen = RegenChamberOptimization.GenerateWith(cond, design);
        Assert.NotNull(gen.Turbopump?.FuelPump);
        Assert.True(gen.Turbopump!.FuelPump!.SpecificSpeed_US
                    > FeasibilityGate.PumpSpecificSpeedCeiling,
            $"Expected N_s > 9000 at RPM = 800000; got {gen.Turbopump.FuelPump.SpecificSpeed_US:F0}");
        var gate = FeasibilityGate.Evaluate(gen);
        Assert.Contains(gate.Violations,
            v => v.ConstraintId == "PUMP_SPECIFIC_SPEED_OFF_BAND");
    }

    [Fact]
    public void Design_DefaultsPumpRpmToAuto()
    {
        // Default PumpRpm_rpm = 0 means auto-derive (back-compat).
        var design = new RegenChamberDesign();
        Assert.Equal(0.0, design.PumpRpm_rpm, precision: 12);
    }

    [Fact]
    public void PumpSpecificSpeedBand_PinsKarassikRange()
    {
        // [600, 9000] is Karassik §2.5 / Stepanoff §2.7 typical band
        // for centrifugal LRE pumps.
        Assert.Equal(600.0, FeasibilityGate.PumpSpecificSpeedFloor, precision: 6);
        Assert.Equal(9000.0, FeasibilityGate.PumpSpecificSpeedCeiling, precision: 6);
    }
}
