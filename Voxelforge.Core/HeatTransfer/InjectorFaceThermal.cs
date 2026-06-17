// InjectorFaceThermal.cs — PHASE 2 (2026-04-20):
// Preliminary-design estimate of the injector face-plate equilibrium
// temperature under steady-state operation.
//
// Why this matters:
//   The injector face is exposed to full-chamber heat flux at x = 0
//   (barrel, Bartz h_g modulated by the barrel-mixing enhancement)
//   and is cooled primarily by the propellants flowing through the
//   bores that pierce it. An under-cooled face burns through in a few
//   hundred milliseconds; a well-cooled face sits at 500–800 K on a
//   typical LOX/HC engine.
//
// Physics model (steady-state lumped):
//   q_in  = h_g   · (T_aw_core − T_face)
//   q_out = h_back · (T_face   − T_prop_avg)
//   Steady:   q_in = q_out
//
//   ⇒  T_face = (h_g · T_aw + h_back · T_prop) / (h_g + h_back)
//
// Coefficients:
//   h_g    = gen.Thermal.Stations[0].h_g_Wm2K  (solver output at x=0)
//   h_back = 0.023 · (k_prop / D_bore) · Re^0.8 · Pr^0.4   (Dittus-Boelter
//           on the element-bore scale; rough but physical in the turbulent
//           limit Re ≥ 4000 typical for LRE injectors).
//
//   Re, Pr built from the INJECTION state (cold fuel, since fuel is usually
//   jacket-cooled and supplies the face). For LOX-rich faces this is
//   pessimistic; a future refinement can blend the two streams.
//
// Fidelity band:
//   ±200 K vs. a real injector face (instrumented test). Sufficient to
//   flag pathological designs (high element density on a small face) and
//   to add a 7th hard gate to the optimizer.
//
// References:
//   Huzel & Huang, AIAA Vol. 147, §8.4 (Injector heat-transfer rule of thumb).
//   Sutton & Biblarz, 9e, §9.6 (Injector thermal protection).

using Voxelforge.Combustion;
using Voxelforge.Coolant;
using Voxelforge.Injector;

namespace Voxelforge.HeatTransfer;

public sealed record InjectorFaceResult(
    double TFace_K,              // predicted equilibrium face temperature
    double TAwCore_K,             // hot gas adiabatic wall T at x=0
    double TPropAvg_K,            // cold-side propellant mean temperature
    double HeatFlux_Wm2,          // net flux into the face at equilibrium
    double HGasSide_Wm2K,         // gas-side HTC at x=0
    double HPropSide_Wm2K,        // propellant-side HTC through the bores
    double FaceArea_cm2,
    double BoreAreaFraction,     // A_bores / A_face
    string Method,               // short label for diagnostics / report
    string[] Warnings,
    // PH-35 (2026-04-29): face material service T limit. Decouples the
    // INJECTOR_FACE_T_EXCEEDED gate from the chamber wall material. Real
    // LRE injector faces are high-temperature alloys (IN625, IN718, SS304)
    // brazed onto a Cu-alloy liner — the face has its own (often lower)
    // T-limit than the gas-side liner. Default 1200 K matches the post-
    // A1-follow-on constant (IN625/SS face); callers can override via
    // `RegenChamberDesign.InjectorFaceMaxTemp_K_Override` when (e.g.) a
    // brazed SS316L face needs a tighter ~1100 K limit.
    double MaxServiceTemp_K = 1200.0);

/// <summary>
/// Physics-only inputs to <see cref="InjectorFaceThermal.Estimate"/>.
/// Carries every field the lumped equilibrium model reads (chamber
/// radius, gas-side HTC + T_aw at x = 0, propellant pair + mass flows,
/// coolant inlet state, wall material, injector pattern + orifice
/// sizing) without coupling the solver to
/// <see cref="Optimization.RegenGenerationResult"/> or any voxel build
/// artefacts. Build via
/// <see cref="Optimization.RegenGenerationResult.ToInjectorFaceGeometry"/>
/// from a generated design, or hand-construct in unit tests.
/// </summary>
public sealed record InjectorFaceGeometry(
    double ChamberRadius_mm,
    double H_g_x0_Wm2K,
    double T_aw_x0_K,
    double T_film_face_x0_K,
    PropellantPair PropellantPair,
    double CoolantInletTemp_K,
    double CoolantInletPressure_Pa,
    int WallMaterialIndex,
    double OxidizerMassFlow_kgs,
    double FuelMassFlow_kgs,
    double TotalMassFlow_kgs,
    InjectorPattern Pattern,
    PatternSizingResult Sizing,
    // PH-36 (2026-04-29): oxidizer-side injection temperature for the
    // flow-weighted T_prop_avg term in the lumped face equilibrium. When
    // 0 (legacy default), `InjectorFaceThermal.Estimate` falls back to
    // the per-pair `DefaultOxidizerInjectionT_K` lookup (90 K for LOX-
    // based pairs; 290-293 K for storables). Set explicitly via
    // `OperatingConditions.OxidizerInletTemp_K` for warm preburner-fed
    // staged combustion or sub-cooled tank conditions.
    double OxidizerInletTemp_K = 0.0,
    // PH-35 (2026-04-29): face material max-service-T override. Default
    // 0 → `InjectorFaceThermal.DefaultInjectorFaceMaxTemp_K` (1200 K, the
    // IN625/SS post-A1 constant). Override > 0 surfaces on the
    // `MaxServiceTemp_K` field of the result and feeds `INJECTOR_FACE_T_EXCEEDED`.
    // Use case: brazed SS316L face on a CuCrZr liner (~1100 K limit).
    double InjectorFaceMaxTemp_K_Override = 0.0,
    // Z3-F4 (2026-04-29): chamber-side Mach for the mixing-layer-effectiveness
    // Mach attenuation. Default 0 → `InjectorFaceThermal.Estimate` falls back
    // to the legacy constant `MixingLayerEffectivenessFor(elementType)` (no
    // Mach awareness). Higher M (small contraction-ratio designs, ε_c ≈ 2.5)
    // thickens the mixing layer and attenuates film protection — see
    // `MixingLayerEffectivenessFor(string?, double)`. Computed by
    // `RegenGenerationResult.ToInjectorFaceGeometry` from station-0 area
    // ratio + chamber γ via the subsonic isentropic area-Mach relation.
    double ChamberMach = 0.0);

public static class InjectorFaceThermal
{
    /// <summary>
    /// Lower bound on the fraction of the face pierced by bores before the
    /// "back-cooled face" model makes sense. At very low bore area (few
    /// elements on a big face) the face is effectively uncooled and our
    /// simple model underestimates T_face. Warn but still return a number.
    /// </summary>
    public const double MinBoreAreaFraction = 0.005;

    /// <summary>
    /// Sprint 37 / PH-13 (2026-04-25): default face-plate thickness used
    /// in the cylindrical-fin-efficiency correction. 4 mm is the median
    /// LPBF-printed LRE injector face thickness — thinner risks structural
    /// failure under chamber pressure, thicker risks wasted mass + slow
    /// heat-up. Future sprints can promote this to a design field if a
    /// designer wants to optimise around it.
    /// </summary>
    public const double DefaultFaceThickness_mm = 4.0;

    /// <summary>
    /// PH-35 (2026-04-29): default injector-face max-service T (K). 1200 K
    /// matches the post-A1-follow-on constant (IN625/SS face material;
    /// SpaceX Merlin face plates per FAA filings; Sutton §6.4 face plate
    /// material selection). Used when
    /// <see cref="InjectorFaceGeometry.InjectorFaceMaxTemp_K_Override"/>
    /// is 0 (legacy). Callers can override per-design via
    /// <c>RegenChamberDesign.InjectorFaceMaxTemp_K_Override</c> when (e.g.)
    /// a brazed SS316L face needs a tighter ~1100 K limit.
    /// </summary>
    public const double DefaultInjectorFaceMaxTemp_K = 1200.0;

    /// <summary>
    /// **Sprint feasibility-audit-C (2026-04-26 evening):** element-type-
    /// dependent mixing-layer effectiveness for the face-thermal film
    /// attenuation. The "effectiveness" here is COMPENSATION for the
    /// lumped-model under-counting of cold propellant entrainment into
    /// the face boundary layer.
    ///
    /// **Sprint feasibility-audit-M (2026-04-27):** Coax + Showerhead
    /// bumped 0.50 → 0.65 to match published Merlin / SSME / F-1 / RD-180
    /// face-T data (~700-1000 K). The original 0.50 "PR #79 baseline" was
    /// a conservative placeholder; production-class coax injectors with
    /// continuous outer film row preserve the boundary-layer film better
    /// than the lumped model captured. Empirical calibration: with mixing
    /// = 0.65, merlin preset face T drops from 1244 K → 960 K (matches
    /// Merlin published face T ~800-900 K to ±100 K). The 0.65 value also
    /// aligns Coax with ImpingingDoublet (which has similar film
    /// preservation in practice).
    ///
    /// **PHYSICS-INTEGRITY DISCLOSURE (2026-04-27):** the 0.65 value is
    /// a calibration constant tuned to match published-engine face-T
    /// descriptions, NOT a derived physics quantity. The original 0.50
    /// was the same (just less calibrated). Both values are "fitting
    /// parameters" used to compensate for the lumped equilibrium model's
    /// systematic under-counting of film entrainment near the face. The
    /// model itself has a documented ±200 K accuracy band (see header
    /// comment of this file) and would benefit from CFD validation
    /// (see Sprint T2.3 in optimization-infrastructure roadmap, currently
    /// unscheduled). See `docs/physics-integrity-notes.md` for the full
    /// catalog of model knowns/unknowns.
    ///
    ///   • Coax / Showerhead: coherent axial spray + outer-row film,
    ///     η = 0.65 (Sprint M calibration; was 0.50 PR #79 baseline).
    ///   • Pintle: radial spray with high turbulence + small bore-cross
    ///     fraction (single central element); the lumped model hugely
    ///     under-counts effective face cooling. Real pintles (TR-201,
    ///     LMDE) run face T 700-800 K — the model predicts 1500+ K with
    ///     η=0.5. η = 0.80.
    ///   • ImpingingDoublet: localised impingement creates bulk turbulence
    ///     that accelerates film mixing → η = 0.65.
    /// Default 0.65 for unknown element types (was 0.50 pre-Sprint-M;
    /// updated to match the new Coax/Showerhead calibration).
    /// </summary>
    public static double MixingLayerEffectivenessFor(string? elementType) => elementType switch
    {
        "Pintle"            => 0.80,
        "ImpingingDoublet"  => 0.65,
        "Coax"              => 0.65,
        "Showerhead"        => 0.65,
        _                   => 0.65,
    };

    /// <summary>
    /// Z3-F4 (2026-04-29) reference chamber Mach number — values above this
    /// trigger the linear attenuation in
    /// <see cref="MixingLayerEffectivenessFor(string?, double)"/>. 0.10 is
    /// the typical large-LRE chamber Mach for ε_c ≈ 6-8 (Sutton 9e §3.3);
    /// designs with smaller contraction ratios (ε_c ≈ 2.5) run M ≈ 0.25
    /// and thus see meaningful attenuation.
    /// </summary>
    public const double ChamberMachReference = 0.10;

    /// <summary>
    /// Z3-F4 (2026-04-29) per-Mach attenuation slope. At M = M_ref the
    /// factor is 1.0; at M = M_ref + 0.4 (≈ε_c = 2.5) the factor is 0.8;
    /// floored at 0.5 to keep the model in a defensible range. Calibration-
    /// grade — see <c>physics-integrity-notes.md</c> Z3-F4.
    /// </summary>
    public const double ChamberMachAttenuationSlope = 0.5;

    /// <summary>
    /// Z3-F4 (2026-04-29) lower clamp on the Mach-attenuated mixing-layer
    /// effectiveness. Prevents pathological designs (e.g. M = 1 chamber)
    /// from collapsing η to zero — the model band would no longer be
    /// defensible there.
    /// </summary>
    public const double MinMachAttenuatedFactor = 0.5;

    /// <summary>
    /// Z3-F4 (2026-04-29): Mach-aware mixing-layer effectiveness. At low
    /// chamber Mach (typical ε_c ≥ 5 designs at M ≈ 0.1) returns the
    /// per-element-type baseline from
    /// <see cref="MixingLayerEffectivenessFor(string?)"/>. At higher chamber
    /// Mach (small ε_c ≈ 2.5 → M ≈ 0.25) the mixing layer thickens and the
    /// effective film protection drops; we attenuate η linearly:
    /// <code>
    ///   η(M) = η_base · max(1 − slope · (M − M_ref), floor)
    /// </code>
    /// with <c>M_ref = 0.10</c>, <c>slope = 0.5</c>, <c>floor = 0.5</c>.
    /// Calibration-grade — paired with PH-37 (film-cooling C* derate, shipped
    /// 2026-04-29) and PH-21 (G_INJ stability gate, shipped earlier). See
    /// <c>physics-integrity-notes.md</c> Z3-F4 for the full disclosure.
    /// </summary>
    public static double MixingLayerEffectivenessFor(string? elementType, double chamberMach)
    {
        double baseEta = MixingLayerEffectivenessFor(elementType);
        if (!(chamberMach > ChamberMachReference)) return baseEta;
        double attenuation = Math.Max(
            1.0 - ChamberMachAttenuationSlope * (chamberMach - ChamberMachReference),
            MinMachAttenuatedFactor);
        return baseEta * attenuation;
    }

    /// <summary>
    /// PH-36 (2026-04-29): per-propellant-pair default oxidizer injection
    /// temperature (K) used as the cold-side reference when
    /// <see cref="InjectorFaceGeometry.OxidizerInletTemp_K"/> isn't supplied.
    /// LOX-based pairs (current production set: LOX/CH4, LOX/H2, LOX/RP-1)
    /// share the LOX boiling point ~90 K. Storable hypergolics deliver
    /// the oxidizer at room temperature. The original hardcoded 90 K was
    /// correct for every production-class design but biased face equilibrium
    /// for future storable / preburner-fed staged combustion paths.
    /// Override via <c>OperatingConditions.OxidizerInletTemp_K</c>; when
    /// > 0, the explicit value short-circuits this lookup.
    /// </summary>
    public static double DefaultOxidizerInjectionT_K(PropellantPair pair) => pair switch
    {
        PropellantPair.LOX_CH4   =>  90.18,  // LOX boiling point at 1 atm
        PropellantPair.LOX_H2    =>  90.18,
        PropellantPair.LOX_RP1   =>  90.18,
        PropellantPair.N2O4_MMH  => 293.15,  // room T (NTO bp ≈ 294 K, stored sub-cooled)
        PropellantPair.H2O2_RP1  => 290.15,  // room T (aqueous H2O2)
        _                        =>  90.18,  // cryogen-conservative fallback (matches legacy hardcode)
    };

    /// <summary>
    /// Estimate the injector face equilibrium temperature from a
    /// physics-only <see cref="InjectorFaceGeometry"/> input record.
    /// Construct the geometry via
    /// <see cref="Optimization.RegenGenerationResult.ToInjectorFaceGeometry"/>
    /// from a generated design, or hand-build it in unit tests. When
    /// <paramref name="fuelInjectionT_K_override"/> is supplied (PHASE 5,
    /// coolant crossover active), it replaces the default fuel-tank inlet
    /// temperature — useful for closed-expander-cycle designs where the
    /// injector sees hot regen-heated fuel instead of cold tank fuel.
    /// </summary>
    public static InjectorFaceResult Estimate(
        InjectorFaceGeometry geom,
        double? fuelInjectionT_K_override = null)
    {
        var pat = geom.Pattern;
        var sizing = geom.Sizing;

        // Sprint 14 / Track I / P9: pre-size at 4 — see RegenCoolingSolver.
        var warnings = new List<string>(4);

        // Hot-side state (x=0 barrel).
        // **Sprint feasibility-audit-3 (2026-04-26):** apply a finite-
        // effectiveness film attenuation to T_aw at the face.
        //
        // Model history:
        //   • PHASE 2 (2026-04-20): used raw T_aw (3,500 K core recovery),
        //     ignored film entirely → T_face predicted 3,000+ K vs real
        //     Merlin-class face at 700-900 K.
        //   • PR #74 (2026-04-26 morning): bore-wall area fix — dropped
        //     to ~2,300 K, still too high.
        //   • This PR: film-effectiveness attenuation — drops to ~700-1,000 K.
        //
        // The honest middle-ground model: real injector faces see partial
        // film protection bounded by:
        //   • coverage = (1 − bore-cross-section fraction) — bare orifice
        //     areas can't be film-protected; ~10-15 % typical
        //   • mixing-layer effectiveness ≈ 0.5 — even directly behind the
        //     film slot, the boundary layer is a mixing layer between
        //     T_film and T_aw, not a pure film at T_film. Naively using
        //     EffectiveRecoveryTemp_K[0] (≈ T_film with η=1.0 at injection)
        //     over-corrects T_face down to ~T_film ≈ 150 K.
        //
        // Combined effective attenuation: filmAttenuation = coverage × 0.5
        // gives ≈ 0.43 for typical injectors. Drops T_aw from 3,500 → 2,070 K,
        // yielding T_face ≈ 700-900 K with bore-back cooling — published range.
        //
        // The 0.5 mixing-layer factor is calibration-grade. Future sprint
        // could tune it against published face-T data per propellant pair.
        double h_g = geom.H_g_x0_Wm2K;
        double T_aw_raw   = geom.T_aw_x0_K;
        double T_film_face = geom.T_film_face_x0_K;        // ≈ T_film with η=1.0 at face station

        // Cold-side state: propellant averages at injection.
        var meta = PropellantPairs.GetMeta(geom.PropellantPair);
        var (oxRho, fuelRho) = OrificeModel.InjectionDensities(geom.PropellantPair);
        // Use the coolant fluid's cold-side state. PHASE 5: when the design
        // has a coolant crossover, the fuel at the injector has already
        // picked up heat in the jacket — use the solver outlet T instead
        // of the inlet T. Defaults to inlet T (open cycle) when not supplied.
        var fluid = CoolantRegistry.Get(meta.CoolantFluidKey);
        double T_fuel_inj = fuelInjectionT_K_override ?? geom.CoolantInletTemp_K;
        var fuelBulk = fluid.GetState(
            T_fuel_inj,
            geom.CoolantInletPressure_Pa);
        // PH-36 (2026-04-29): per-pair oxidizer injection T. When the
        // caller supplies `OxidizerInletTemp_K > 0` (via
        // `OperatingConditions.OxidizerInletTemp_K`), use it; otherwise
        // fall back to the per-pair default (90 K LOX boiling point for
        // LOX-based pairs; 290-293 K for storable hypergolics). All
        // current production designs use LOX → 90 K (no functional
        // change to existing fixtures).
        double T_ox_inj = geom.OxidizerInletTemp_K > 0
            ? geom.OxidizerInletTemp_K
            : DefaultOxidizerInjectionT_K(geom.PropellantPair);
        // Flow-weighted mean.
        double T_prop_avg =
            (geom.OxidizerMassFlow_kgs * T_ox_inj
           + geom.FuelMassFlow_kgs     * T_fuel_inj)
          / Math.Max(geom.TotalMassFlow_kgs, 1e-9);

        // h_back via Dittus-Boelter on the bore-velocity scale. Use the
        // largest of ox / fuel per-element diameter because the largest
        // orifice in an element dominates the back-side convection.
        //
        // 2026-04-26 NOTE: an attempt to add a parallel LOX-side h_back
        // contribution (LOX-density / LOX-viscosity / saturated-LOX k)
        // weighted by per-stream wall areas was reverted because it
        // surprisingly REDUCED h_back on most presets — likely because
        // the LOX velocity used by InjectorSizing is markedly lower than
        // fuel velocity (mass flow / density: LOX ρ ~ 2.7× CH4 ρ → same
        // ṁ implies ~1/2.7 the velocity). The weighted average dragged
        // the effective h_back down even though LOX-side k + density are
        // higher. A real fix needs per-stream parallel heat-flux balance
        // (sum of two ΔT contributions, not weighted-avg of two HTCs)
        // — out of scope for this PR. The fuel-only approach below is
        // pessimistic in the right direction (over-predicts T_face).
        double D_ox_mm    = sizing.PerElementResult.OxEquivDiameter_mm;
        double D_fuel_mm  = sizing.PerElementResult.FuelEquivDiameter_mm;
        double D_bore_mm  = Math.Max(D_ox_mm, D_fuel_mm);
        double D_bore_m   = Math.Max(D_bore_mm * 1e-3, 1e-4);
        double v_bore_raw = sizing.PerElementResult.FuelVelocity_ms > 0
                          ? sizing.PerElementResult.FuelVelocity_ms
                          : sizing.PerElementResult.OxVelocity_ms;
        // L5 (post-Phase-6 logical-error audit): bumped 1.0 → 10.0 m/s.
        // Real injector bore velocities run 10-50 m/s; the prior 1.0 m/s
        // floor was orders below realistic and silently inflated T_face
        // by 200-500 K when the upstream sizer produced a degenerate
        // (zero-velocity) PerElementResult. Floor at 10 m/s + warning so
        // the upstream regression is visible.
        const double InjectorBoreVelocityFloor_ms = 10.0;
        double v_bore = Math.Max(v_bore_raw, InjectorBoreVelocityFloor_ms);
        if (v_bore_raw < InjectorBoreVelocityFloor_ms)
            warnings.Add($"Injector bore velocity raw {v_bore_raw:F1} m/s below "
                       + $"{InjectorBoreVelocityFloor_ms:F0} m/s floor — upstream "
                       + "sizer may have produced degenerate orifice areas. "
                       + "T_face uses the floored velocity (Re_bore raised); "
                       + "re-check FuelVelocity_ms / OxVelocity_ms on PerElementResult.");
        double Re_bore = fuelBulk.Density_kgm3 * v_bore * D_bore_m
                       / Math.Max(fuelBulk.Viscosity_PaS, 1e-8);
        double Pr_fuel = Math.Max(fuelBulk.Prandtl, 1e-3);
        double Nu = 0.023 * Math.Pow(Re_bore, 0.8) * Math.Pow(Pr_fuel, 0.4);
        double h_back = Nu * fuelBulk.Conductivity_WmK / D_bore_m;

        // Face + bore-area geometry.
        double rChamber_mm = geom.ChamberRadius_mm;
        double A_face_m2  = Math.PI * (rChamber_mm * 1e-3) * (rChamber_mm * 1e-3);
        double A_face_cm2 = A_face_m2 * 1e4;
        double A_bores_cross_m2 = (sizing.TotalOxArea_mm2 + sizing.TotalFuelArea_mm2) * 1e-6;
        double boreCrossFrac    = A_bores_cross_m2 / Math.Max(A_face_m2, 1e-9);

        if (boreCrossFrac < MinBoreAreaFraction)
            warnings.Add($"Face bore-area fraction {boreCrossFrac:P2} is below {MinBoreAreaFraction:P1} — "
                       + "T_face estimate is a lower bound; real face may run hotter.");

        // **Sprint feasibility-audit (2026-04-26):** weight h_back by the bore
        // WALL area (lateral surface inside the bore where Dittus-Boelter
        // convection actually happens), NOT the bore cross-section area.
        // Pre-fix the code used the cross-section area, which under-counts the
        // convective surface by ~6× (typical D ≈ 5 mm, L ≈ 4 mm gives a
        // wall-area-to-cross-section ratio of 4L/D ≈ 3.2 per orifice,
        // multiplied by 2 for ox+fuel pairs). The error showed up as injector
        // face T predicted at 3,000+ K on every canonical preset, where real
        // engines with comparable bore geometry run at 700-900 K. The
        // post-fix prediction sits at 1,200-1,800 K — closer to but still
        // hotter than reality (the residual gap is film cooling, which the
        // MVP doesn't model — Sprint 37 PH-13 added the fin correction but
        // not the film term).
        //
        // Per-element bore wall area = π × D_bore × L_face per orifice. For an
        // element with both ox + fuel orifices, multiply by 2. Total bore wall
        // area = element_count × (D_ox + D_fuel) × π × L_face.
        double L_face_m  = DefaultFaceThickness_mm * 1e-3;
        double A_bore_wall_per_element_m2 =
            Math.PI * (D_ox_mm + D_fuel_mm) * 1e-3 * L_face_m;
        double A_bore_wall_total_m2 = pat.ElementCount * A_bore_wall_per_element_m2;
        double boreWallFrac = A_bore_wall_total_m2 / Math.Max(A_face_m2, 1e-9);
        double h_back_eff_raw = h_back * Math.Max(boreWallFrac, MinBoreAreaFraction);

        // Sprint 37 / PH-13 (2026-04-25): cylindrical-fin efficiency on
        // the bore wall acting as a conductive path between the back-
        // cooled bore (h_back) and the gas-loaded face top (h_g). The
        // pre-Sprint-37 area-weighted h_back implicitly assumed a perfect
        // fin (η = 1); real injector faces have a small temperature drop
        // along the bore wall over the face thickness L.
        //
        // Rectangular-fin approximation:
        //   m = √(2·h_back / (k_wall · t_fin))
        //   η = tanh(m·L) / (m·L)
        // where L = face thickness and t_fin ≈ (element pitch − bore d) / 2
        // is the half-pitch wall thickness between adjacent bores.
        //
        // For typical CuCrZr at face working T (~700 K) with L = 4 mm and
        // t_fin = 1 mm, η ≈ 0.85; the correction lowers h_back_eff by
        // ~15 %. Captures the leading-order departure from the perfect-
        // fin assumption — Huzel & Huang §8.4 cites ±100-300 K on T_face
        // and PH-13 attributes a meaningful fraction of that to the
        // missing fin treatment.
        double L_fin_m = DefaultFaceThickness_mm * 1e-3;
        double pitch_mm = pat.ElementCount > 0
            ? 2.0 * Math.Sqrt(A_face_m2 / (Math.PI * pat.ElementCount)) * 1000.0
            : 4.0 * D_bore_mm; // sparse-pattern fallback
        double t_fin_mm = Math.Max((pitch_mm - D_bore_mm) * 0.5, 0.3);
        double t_fin_m = t_fin_mm * 1e-3;
        var wallMat = HeatTransfer.WallMaterials.All[
            Math.Clamp(geom.WallMaterialIndex,
                       0, HeatTransfer.WallMaterials.All.Length - 1)];
        double k_wall = Math.Max(wallMat.ConductivityAt(700.0), 1.0);
        double m_fin = Math.Sqrt(
            2.0 * h_back / Math.Max(k_wall * t_fin_m, 1e-9));
        double mL = m_fin * L_fin_m;
        double eta_fin = mL > 1e-6 ? Math.Tanh(mL) / mL : 1.0;
        double h_back_eff = h_back_eff_raw * eta_fin;

        // **Sprint feasibility-audit-3 (2026-04-26):** finite-effectiveness
        // film attenuation. See block comment near s0 declaration above.
        // T_aw_eff = T_aw - filmAttenuation × (T_aw - T_film_face), where
        //   filmAttenuation = coverage × MixingLayerEffectiveness
        //   coverage        = 1 - boreCrossFrac (bare orifices unprotected)
        //   MixingLayerEffectiveness = element-type dependent
        //
        // **Sprint feasibility-audit-C (2026-04-26 evening):** element-type
        // dependent mixing-layer effectiveness. Pre-fix used 0.5 universally
        // — calibrated for showerhead/coax injectors where the spray pattern
        // mostly preserves a coherent film layer at the face. Pintle injectors
        // (single central element with radial spokes) DISRUPT the boundary
        // layer at the face — the central pintle protrudes into the chamber,
        // its outer surface is wetted by film but the chamber-side face area
        // sees direct gas impingement. Effective mixing for pintle is much
        // more aggressive (η_film ≈ 0.3 instead of 0.5).
        // Impinging doublets fall in between (0.6) — the impingement points
        // create localised hot spots but the bulk face is film-protected.
        // Z3-F4 (2026-04-29): when ChamberMach > 0 (typical population by
        // ToInjectorFaceGeometry from the contour station-0 area ratio),
        // attenuate the mixing-layer effectiveness against the reference
        // M = 0.10. Pre-Z3-F4 used the constant per-element-type factor
        // unconditionally — small-ε_c chamber designs (M ≈ 0.25 at ε_c ≈ 2.5)
        // had no Mach-awareness in the face thermal model.
        double mixingEff = geom.ChamberMach > 0
            ? MixingLayerEffectivenessFor(pat.ElementType, geom.ChamberMach)
            : MixingLayerEffectivenessFor(pat.ElementType);
        double coverage = 1.0 - boreCrossFrac;
        double filmAttenuation = coverage * mixingEff;
        double T_aw = T_aw_raw - filmAttenuation * (T_aw_raw - T_film_face);

        // Steady equilibrium between gas-side and prop-side.
        double T_face = (h_g * T_aw + h_back_eff * T_prop_avg)
                      / Math.Max(h_g + h_back_eff, 1e-9);
        double q_face = h_g * (T_aw - T_face);

        // PH-35 (2026-04-29): face material max-T. Override > 0 short-
        // circuits the IN625/SS default (1200 K) with a per-design value
        // (e.g. 1100 K for a brazed SS316L face on CuCrZr liner).
        double maxServiceT_K = geom.InjectorFaceMaxTemp_K_Override > 0
            ? geom.InjectorFaceMaxTemp_K_Override
            : DefaultInjectorFaceMaxTemp_K;

        return new InjectorFaceResult(
            TFace_K: T_face,
            TAwCore_K: T_aw,
            TPropAvg_K: T_prop_avg,
            HeatFlux_Wm2: q_face,
            HGasSide_Wm2K: h_g,
            HPropSide_Wm2K: h_back_eff,
            FaceArea_cm2: A_face_cm2,
            BoreAreaFraction: boreCrossFrac,
            Method: "Lumped steady, D-B on bore scale, coverage-weighted T_aw",
            Warnings: warnings.ToArray(),
            MaxServiceTemp_K: maxServiceT_K);
    }
}
