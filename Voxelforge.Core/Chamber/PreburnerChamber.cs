// PreburnerChamber.cs — Preburner combustion-chamber sizing for
// staged-combustion cycles.
//
// Scope
// ─────
// Preburner (also called "gas generator" in GG cycle, "preburner" in
// staged-combustion) is a miniature combustion chamber operating at
// an off-nominal MR (fuel-rich OR ox-rich) to produce warm, turbine-
// drivable gas without completely consuming the propellant. The warm
// gas drives pumps; downstream, the preburner exhaust either (a)
// dumps overboard (gas generator), or (b) feeds the main chamber
// (staged combustion / FFSC).
//
// Key knobs
// ─────────
//   • Mixture ratio — fuel-rich for LOX/CH4 staged: MR ≈ 0.5 – 0.8
//     (T_c ≈ 900-1100 K, turbine-safe); ox-rich for LOX/RP1 staged:
//     MR ≈ 20-40 (T_c ≈ 700-900 K — "soft" ox-rich regime).
//   • Chamber pressure — typically 1.2 – 1.8 × main-chamber Pc.
//   • Turbine-inlet temperature target — usually ≤ 1100 K for
//     uncooled turbine wheels; higher for cooled.
//
// Model fidelity
// ──────────────
// This MVP reuses the main `PropellantTables.Lookup` CEA interpolator
// at the preburner's (MR, Pc). Returns stagnation T_c, C*, γ, MW.
// The preburner chamber volume is sized for a characteristic L*
// appropriate for gas-generator combustion (typically 0.3-0.5 m; we
// default to 0.40 m). Wall temperature + regen cooling of the
// preburner is deferred to a later follow-on when the staged-
// combustion narrative demands it.
//
// FFSC dual-preburner split
// ─────────────────────────
// Full-flow staged combustion (Raptor-class) uses TWO preburners: a
// fuel-rich one (drives the fuel-pump turbine) + an ox-rich one
// (drives the ox-pump turbine). Both exhausts feed the main chamber.
// `SizeFfscDual` returns both records; the single-preburner
// `Size(FullFlow, ...)` path still works (represents the fuel-rich
// side only) but no longer emits the "MVP" warning when the
// dual-call variant is used.
//
// Not modelled here
// ─────────────────
//   • Preburner wall-temp feasibility gate (gate only covers
//     aerospike plug today)
//   • Ignition energy for the preburner igniter (the existing igniter
//     presets apply but aren't re-evaluated)
//   • Active regen cooling of the preburner chamber itself
//     (voxel geometry is present but unchilled)

using Voxelforge.Combustion;
using Voxelforge.FeedSystem;

namespace Voxelforge.Chamber;

/// <summary>
/// Result of a preburner sizing pass. Warm-gas state drives the
/// turbopump turbine; downstream routing depends on
/// <see cref="FeedSystem.EngineCycle"/>.
/// </summary>
public sealed record PreburnerResult(
    EngineCycle Cycle,
    double MixtureRatio,
    double ChamberPressure_Pa,
    double WarmGasTemperature_K,     // stagnation T_c of the preburner
    double WarmGasCStar_ms,
    double WarmGasGamma,
    double WarmGasMolecularWeight,
    double MassFlow_kgs,              // total mass flow through preburner
    double CharacteristicLength_m,
    double ChamberVolume_mm3,
    string Notes,
    string[] Warnings,
    // Sprint 9 Track B (2026-04-22) — optional preburner-wall thermal
    // result. Populated by RegenChamberOptimization.GenerateWith when
    // RegenChamberDesign.IncludePreburnerRegenCooling is true AND the
    // cycle has a preburner. Consumed by the PREBURNER_WALL_TEMP
    // feasibility gate. Null on the pre-Sprint-9 adiabatic-wall path.
    HeatTransfer.PreburnerThermalResult? Thermal = null);

/// <summary>
/// Preburner sizing stub. Pure math; no PicoGK / filesystem
/// dependency. Thread-safe.
/// </summary>
public static class PreburnerChamber
{
    /// <summary>Default L* for a gas-generator / preburner chamber (m).</summary>
    public const double DefaultCharacteristicLength_m = 0.40;

    /// <summary>
    /// Turbine-safe warm-gas temperature ceiling (K). Uncooled
    /// turbine wheels tolerate ≤ 1100 K; preburner MR is chosen so
    /// the resulting T_c is at or below this. Warning emitted
    /// otherwise.
    /// </summary>
    public const double TurbineInletTempLimit_K = 1100.0;

    /// <summary>
    /// Size a preburner for the given cycle, propellant pair, and
    /// main-chamber flow context.
    /// </summary>
    /// <param name="cycle">Engine cycle. Returns null for PressureFed / ElectricPump / OpenExpander (no preburner).</param>
    /// <param name="pair">Propellant pair (must be implemented).</param>
    /// <param name="preburnerMr">Mixture ratio at the preburner (fuel-rich or ox-rich).</param>
    /// <param name="preburnerPc_Pa">Preburner chamber pressure (Pa).</param>
    /// <param name="turbineMassFlow_kgs">Total mass flow through the preburner → turbine.</param>
    /// <param name="characteristicLength_m">L* override; 0 uses the default.</param>
    /// <param name="suppressFfscSingleMvpWarning">
    /// When the caller is a `SizeFfscDual` wrapper that emits two
    /// explicit results, the "single-preburner MVP" warning is no
    /// longer informative. Set to true to suppress it. Default false
    /// preserves legacy single-call behaviour.
    /// </param>
    public static PreburnerResult? Size(
        EngineCycle cycle,
        PropellantPair pair,
        double preburnerMr,
        double preburnerPc_Pa,
        double turbineMassFlow_kgs,
        double characteristicLength_m = 0,
        bool suppressFfscSingleMvpWarning = false)
    {
        // Null on cycles that don't use a preburner.
        if (cycle is EngineCycle.PressureFed
                  or EngineCycle.ElectricPump
                  or EngineCycle.OpenExpander)
            return null;

        if (preburnerPc_Pa <= 0)
            throw new System.ArgumentOutOfRangeException(nameof(preburnerPc_Pa),
                "preburner chamber pressure must be positive");
        if (turbineMassFlow_kgs <= 0)
            throw new System.ArgumentOutOfRangeException(nameof(turbineMassFlow_kgs),
                "turbine mass flow must be positive");
        if (preburnerMr <= 0)
            throw new System.ArgumentOutOfRangeException(nameof(preburnerMr),
                "preburner MR must be positive");

        double lStar = characteristicLength_m > 0
            ? characteristicLength_m : DefaultCharacteristicLength_m;

        var warnings = new System.Collections.Generic.List<string>();

        // Look up combustion gas at the preburner's off-nominal MR +
        // Pc. PropellantTables.Lookup handles MRs outside the peak-C*
        // band by linearly extrapolating within the pair table.
        var gas = PropellantTables.Lookup(pair, preburnerMr, preburnerPc_Pa);

        if (gas.ChamberTemp_K > TurbineInletTempLimit_K)
        {
            warnings.Add($"Preburner T_c {gas.ChamberTemp_K:F0} K exceeds "
                       + $"{TurbineInletTempLimit_K:F0} K turbine-safe ceiling. "
                       + $"Soften preburner MR (go further from stoichiometric) "
                       + $"or add turbine-wheel cooling.");
        }

        if (cycle == EngineCycle.FullFlow && !suppressFfscSingleMvpWarning)
        {
            warnings.Add("FullFlow cycle: single-preburner call returns only one "
                       + "preburner. True FFSC has ox-rich + fuel-rich preburners; "
                       + "call PreburnerChamber.SizeFfscDual for both records.");
        }

        // Chamber volume V_c = L* × A_t where A_t = F / (C* × Pc) for
        // the preburner — but preburner "thrust" is not a design
        // variable (the preburner has no nozzle). Instead: size from
        // mass-flow conservation: A_t = m_dot × C* / Pc.
        double throatArea_m2 = turbineMassFlow_kgs * gas.CStar_ms / preburnerPc_Pa;
        double chamberVolume_m3 = lStar * throatArea_m2;
        double chamberVolume_mm3 = chamberVolume_m3 * 1e9;

        string notes = cycle switch
        {
            EngineCycle.GasGenerator =>
                $"Gas-generator preburner — fuel-rich @ MR={preburnerMr:F2}. "
                + $"Warm-gas T={gas.ChamberTemp_K:F0} K feeds turbine; exhaust overboard.",
            EngineCycle.StagedCombustion =>
                $"Staged-combustion preburner — MR={preburnerMr:F2}. "
                + $"Warm-gas T={gas.ChamberTemp_K:F0} K drives turbine → main chamber.",
            EngineCycle.FullFlow =>
                $"FFSC preburner (one of two — call SizeFfscDual for the full pair) — "
                + $"MR={preburnerMr:F2}. Warm-gas T={gas.ChamberTemp_K:F0} K.",
            _ => "",
        };

        return new PreburnerResult(
            Cycle:                   cycle,
            MixtureRatio:            preburnerMr,
            ChamberPressure_Pa:      preburnerPc_Pa,
            WarmGasTemperature_K:    gas.ChamberTemp_K,
            WarmGasCStar_ms:         gas.CStar_ms,
            WarmGasGamma:            gas.Gamma,
            WarmGasMolecularWeight:  gas.MolecularWeight,
            MassFlow_kgs:            turbineMassFlow_kgs,
            CharacteristicLength_m:  lStar,
            ChamberVolume_mm3:       chamberVolume_mm3,
            Notes:                   notes,
            Warnings:                warnings.ToArray());
    }

    /// <summary>
    /// Suggest a preburner MR for the given cycle + propellant pair.
    /// Defaults follow Sutton RPE 9e Table 10-3 (typical preburner
    /// MRs by cycle).
    /// </summary>
    public static double SuggestPreburnerMr(EngineCycle cycle, PropellantPair pair)
    {
        if (cycle is EngineCycle.PressureFed
                  or EngineCycle.ElectricPump
                  or EngineCycle.OpenExpander)
            return 0;

        // Fuel-rich preburners are standard for kerosene + methane
        // cycles (keeps turbine T low; soot risk managed by
        // downstream injector). Ox-rich preburners are used in the
        // Russian RD-180 class LOX/RP-1 staged designs.
        return pair switch
        {
            PropellantPair.LOX_CH4 => 0.60,   // fuel-rich, T_c ≈ 850 K at Pc=10 MPa
            PropellantPair.LOX_H2  => 0.80,   // fuel-rich, LH2 very tolerant
            PropellantPair.LOX_RP1 => 0.40,   // fuel-rich, kerosene — lower MR to stay below coking
            _                      => 0.60,
        };
    }

    /// <summary>
    /// Suggest an ox-rich preburner MR for full-flow staged combustion
    /// or ORSC. Ox-rich preburners sit far above stoichiometric so T_c
    /// falls into the turbine-safe band (≤ 1100 K) via excess-ox
    /// dilution. Defaults track published flight-engine values:
    /// <list type="bullet">
    ///   <item><b>LOX/RP-1 → 58</b> per RD-180 / RD-191 published spec
    ///         (turbine inlet ~770 K). Glushko / NPO Energomash legacy
    ///         is the only flight ox-rich kerosene cycle.</item>
    ///   <item><b>LOX/CH4 → 60</b> estimated for Raptor-class FFSC
    ///         (SpaceX has not published exact ORP MR; ~55-65 range
    ///         derived from public flow-balance estimates).</item>
    ///   <item><b>LOX/H2 → 150</b> theoretical only — no flight engine
    ///         uses LOX/H2 ORSC. Retained as a placeholder; the
    ///         dilution requirement is huge because LH2 combustion is
    ///         already very tolerant.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Audit note (PH-27):</b> at MR &gt; 8 the underlying
    /// <see cref="Combustion.IPropellantTable"/> is extrapolating into
    /// ox-rich territory (CEA tables are typically tabulated for
    /// MR ∈ [0.5, 8]). Warm-gas T_c at MR = 58 from a 1-D table extended
    /// by log-slopes may be quantitatively unreliable — only its
    /// monotone direction is trustworthy. The
    /// <c>ORSC_PREBURNER_OXCORROSION</c> gate fires on T_c relative to
    /// (material service limit − 50 K) and inherits this uncertainty.
    /// Designs near the corrosion threshold should validate T_c against
    /// CEA directly before fabrication.
    /// </para>
    /// <para>
    /// Pre-2026-04-30 values (LOX/CH4 = 35, LOX/RP-1 = 25) were
    /// pedagogical defaults that under-estimated real-engine MR to keep
    /// the synthetic T_c output away from gate thresholds. They have
    /// been tightened to literature values; the table-extrapolation
    /// uncertainty is now disclosed via the <see cref="PreburnerResult.Notes"/>
    /// field rather than hidden by conservative MR choices.
    /// </para>
    /// </remarks>
    public static double SuggestOxRichPreburnerMr(PropellantPair pair) => pair switch
    {
        PropellantPair.LOX_CH4 => 60.0,    // Raptor ORP estimate (~55-65 range)
        PropellantPair.LOX_H2  => 150.0,   // theoretical placeholder; no flight ORSC LOX/H2 engine
        PropellantPair.LOX_RP1 => 58.0,    // RD-180 / RD-191 published value
        _                      => 60.0,
    };

    /// <summary>
    /// Threshold above which the <see cref="Combustion.IPropellantTable"/>
    /// is extrapolating into ox-rich territory beyond its calibration
    /// data (CEA tables typically tabulated MR ∈ [0.5, 8]). Used to
    /// flag <see cref="PreburnerResult.Notes"/> when an ORSC / FFSC
    /// preburner runs in the extrapolated regime so downstream consumers
    /// can attach an uncertainty disclosure to the warm-gas T_c output.
    /// </summary>
    /// <remarks>
    /// PH-27 (2026-04-30): not a feasibility gate — the gate would fire
    /// on every flight-realistic ORSC engine (RD-180 at MR=58, Raptor ORP
    /// at MR≈60), which would either be noise or force users to override.
    /// A <c>Notes</c> annotation is the calibrated-yellow-flag pattern
    /// used elsewhere (Z3-F4 Mach attenuation, PH-42 aerospike M(x)).
    /// </remarks>
    public const double OrscTableExtrapolationMrThreshold = 8.0;

    /// <summary>
    /// FFSC dual-preburner split. Returns (fuel-rich, ox-rich)
    /// preburners for a full-flow staged-combustion cycle. The
    /// fuel-rich preburner drives the fuel pump turbine; the ox-rich
    /// preburner drives the ox pump turbine. Both exhausts feed the
    /// main combustion chamber.
    ///
    /// Mass-flow split (exact solve): fuel is partitioned between the
    /// two preburners (f_fr going to fuel-rich, f_or to ox-rich); the
    /// remaining ox goes wherever needed to hit the target preburner
    /// MR on each side. Solving the 2×2 linear system (conserve fuel +
    /// conserve ox) with preburner-MR definitions yields the
    /// closed-form partition
    ///   f_fr = M_fuel · (MR_or − MR_overall) / (MR_or − MR_fr)
    ///   f_or = M_fuel − f_fr
    ///   o_fr = MR_fr · f_fr ;  o_or = MR_or · f_or
    ///   FR_mdot = f_fr + o_fr ;  OR_mdot = f_or + o_or
    /// where MR_overall = M_ox / M_fuel. This conserves both
    /// propellants exactly (FR_mdot + OR_mdot = M_fuel + M_ox) and
    /// correctly models the Raptor-class topology where each preburner
    /// takes ~95 % of its dominant propellant + ~5 % of the counter-
    /// propellant as dilutant. An earlier MVP approximation
    /// (FR = M_fuel · (1 + MR_fr), OR = M_ox · (1 + 1/MR_or)) assumed
    /// 100 %/0 % splits and over-allocated mass-flow by ~5-20 %.
    /// </summary>
    /// <param name="pair">Propellant pair (must be implemented).</param>
    /// <param name="fuelRichMr">
    /// Mixture ratio on the fuel-rich side. 0 resolves to
    /// <see cref="SuggestPreburnerMr"/> for the pair.
    /// </param>
    /// <param name="oxRichMr">
    /// Mixture ratio on the ox-rich side. 0 resolves to
    /// <see cref="SuggestOxRichPreburnerMr"/> for the pair.
    /// </param>
    /// <param name="preburnerPc_Pa">Preburner chamber pressure (Pa).</param>
    /// <param name="totalFuelMassFlow_kgs">Total engine fuel mdot.</param>
    /// <param name="totalOxMassFlow_kgs">Total engine oxidiser mdot.</param>
    /// <param name="characteristicLength_m">L* override; 0 uses the default.</param>
    public static (PreburnerResult FuelRich, PreburnerResult OxRich) SizeFfscDual(
        PropellantPair pair,
        double fuelRichMr,
        double oxRichMr,
        double preburnerPc_Pa,
        double totalFuelMassFlow_kgs,
        double totalOxMassFlow_kgs,
        double characteristicLength_m = 0)
    {
        if (preburnerPc_Pa <= 0)
            throw new System.ArgumentOutOfRangeException(nameof(preburnerPc_Pa),
                "preburner chamber pressure must be positive");
        if (totalFuelMassFlow_kgs <= 0)
            throw new System.ArgumentOutOfRangeException(nameof(totalFuelMassFlow_kgs),
                "total fuel mass flow must be positive");
        if (totalOxMassFlow_kgs <= 0)
            throw new System.ArgumentOutOfRangeException(nameof(totalOxMassFlow_kgs),
                "total ox mass flow must be positive");

        double frMr = fuelRichMr > 0 ? fuelRichMr : SuggestPreburnerMr(EngineCycle.FullFlow, pair);
        double orMr = oxRichMr   > 0 ? oxRichMr   : SuggestOxRichPreburnerMr(pair);

        // Exact 2×2 linear solve (see XML summary above).
        // Degenerate cases are defensively guarded — in practice
        // orMr ≈ 35, frMr ≈ 0.6, overallMr ≈ 3.5, so
        // (orMr − frMr) ≈ 34 and the denominator never approaches 0.
        double overallMr = totalOxMassFlow_kgs / totalFuelMassFlow_kgs;
        double denom = orMr - frMr;
        if (System.Math.Abs(denom) < 1e-9)
            throw new System.ArgumentException(
                $"FFSC mass-flow split is degenerate when fuel-rich MR ({frMr}) "
              + $"equals ox-rich MR ({orMr}).", nameof(oxRichMr));

        double fuelToFuelRich = totalFuelMassFlow_kgs * (orMr - overallMr) / denom;
        double fuelToOxRich   = totalFuelMassFlow_kgs - fuelToFuelRich;
        double oxToFuelRich   = frMr * fuelToFuelRich;
        double oxToOxRich     = orMr * fuelToOxRich;
        double fuelRichMdot   = fuelToFuelRich + oxToFuelRich;
        double oxRichMdot     = fuelToOxRich   + oxToOxRich;

        var fr = Size(
            cycle:                        EngineCycle.FullFlow,
            pair:                         pair,
            preburnerMr:                  frMr,
            preburnerPc_Pa:               preburnerPc_Pa,
            turbineMassFlow_kgs:          fuelRichMdot,
            characteristicLength_m:       characteristicLength_m,
            suppressFfscSingleMvpWarning: true)!;

        var or = Size(
            cycle:                        EngineCycle.FullFlow,
            pair:                         pair,
            preburnerMr:                  orMr,
            preburnerPc_Pa:               preburnerPc_Pa,
            turbineMassFlow_kgs:          oxRichMdot,
            characteristicLength_m:       characteristicLength_m,
            suppressFfscSingleMvpWarning: true)!;

        // Retag the notes so UI / reports can distinguish the pair.
        // PH-27 (2026-04-30): append a table-extrapolation flag on the
        // ox-rich side when MR exceeds the PropellantTable calibration
        // band (≤ ~8 in CEA-derived tables). Flight ORSC engines run
        // MR ≈ 58 (RD-180) so this fires on every realistic ORSC design.
        fr = fr with
        {
            Notes = $"FFSC fuel-rich preburner — MR={frMr:F2}, "
                  + $"T_c={fr.WarmGasTemperature_K:F0} K drives fuel pump turbine; "
                  + $"exhaust feeds main chamber.",
        };
        string orNote = $"FFSC ox-rich preburner — MR={orMr:F2}, "
                      + $"T_c={or.WarmGasTemperature_K:F0} K drives ox pump turbine; "
                      + $"exhaust feeds main chamber.";
        if (orMr > OrscTableExtrapolationMrThreshold)
        {
            orNote += $" [PH-27 disclosure: MR > {OrscTableExtrapolationMrThreshold:F0} extrapolates "
                    + $"PropellantTables beyond CEA calibration band; T_c uncertainty ~±100-200 K. "
                    + $"Validate against direct CEA before fabrication if near corrosion threshold.]";
        }
        or = or with { Notes = orNote };

        return (FuelRich: fr, OxRich: or);
    }
}
