// InjectorPattern.cs — Full injector pattern descriptor.
//
// Combines an element type + count + layout geometry + film-cooling
// fraction into a single record that can be attached to a
// RegenChamberDesign. Because IInjectorElement is an interface,
// InjectorPattern is NOT serialised to JSON by DesignPersistence —
// it is marked [JsonIgnore] on the design record. Persistence of
// injector pattern details is a future schema-version concern.
//
// Film cooling coupling:
//   OuterRowFilmFraction is the fraction of the TOTAL FUEL flow
//   that is drawn from the outermost element ring and directed to
//   the wall film slot. When this value is > 0 and the pattern is
//   set, GenerateWith overrides the design's FilmCooling.FuelFractionAsFilm
//   with this value and enables film cooling. The rest of the film
//   parameters (decay coefficient, slot height, etc.) are taken from
//   the design's FilmCoolingInputs as-is.
//
// Voxel integration:
//   ChamberVoxelBuilder's flange step checks for a non-null pattern.
//   When present it adds element orifice bores through the injector face:
//   one combined bore per element (for CoaxElement / ImpingingDoublet,
//   the combined ox+fuel area is used as the bore size). When absent,
//   the existing two-hole pattern (LOX +Y, fuel -Y) is used unchanged.

using Voxelforge.Injector.Elements;
using Voxelforge.Optimization;

namespace Voxelforge.Injector;

/// <summary>
/// Minimal flow-rate context passed to the voxel builder so it can size
/// element orifices without pulling in the full Optimization namespace.
/// </summary>
public readonly record struct InjectorFlowContext(
    double TotalOxFlow_kgs,
    double TotalFuelFlow_kgs,
    double ChamberPressure_Pa,
    Combustion.PropellantPair PropellantPair,
    double DeltaPInjFraction = 0.20);  // ΔP_inj / Pc; 20% is the nominal default

/// <summary>
/// Sized result for a complete injector pattern (all elements combined).
/// Produced by <see cref="InjectorPattern.SizePattern"/>.
/// </summary>
public sealed record PatternSizingResult(
    int ElementCount,
    OrificeResult PerElementResult,
    double TotalOxArea_mm2,
    double TotalFuelArea_mm2,
    double FlowSplitCheck,      // total computed flow / target total flow (should ≈ 1.0)
    string[] Warnings);

/// <summary>
/// Full injector element pattern for one thrust chamber design.
/// </summary>
public sealed record InjectorPattern
{
    /// <summary>
    /// String key for the element type ("Coax", "ImpingingDoublet", …).
    /// Stored as a plain string so the record remains JSON-serialisable
    /// for future persistence support.
    /// </summary>
    public string ElementType { get; init; } = "Coax";

    /// <summary>Total number of injector elements.</summary>
    [SaDesignVariable(index: 13, min: 8.0, max: 48.0, gate: SaGate.InjectorPatternPresent)]
    public int ElementCount { get; init; } = 20;

    /// <summary>
    /// Pitch-circle radius for element placement [mm].
    /// When ≤ 0, ChamberVoxelBuilder auto-computes it as 0.60 × R_chamber.
    /// </summary>
    public double PitchCircleRadius_mm { get; init; } = 0.0;

    /// <summary>
    /// Fraction of total fuel mass flow directed from the outermost
    /// element ring to the chamber-wall film cooling slot. Range [0, 0.30].
    /// When > 0 and this pattern is active, GenerateWith enables film
    /// cooling with FuelFractionAsFilm = OuterRowFilmFraction.
    /// </summary>
    [SaDesignVariable(index: 15, min: 0.0, max: 0.15, gate: SaGate.InjectorPatternPresent)]
    public double OuterRowFilmFraction { get; init; } = 0.0;

    /// <summary>Discharge coefficient, oxidiser orifice.</summary>
    [SaDesignVariable(index: 16, min: 0.40, max: 0.95, gate: SaGate.InjectorPatternPresent)]
    public double CdOx { get; init; } = OrificeModel.DefaultCd;

    /// <summary>Discharge coefficient, fuel orifice.</summary>
    [SaDesignVariable(index: 17, min: 0.40, max: 0.95, gate: SaGate.InjectorPatternPresent)]
    public double CdFuel { get; init; } = OrificeModel.DefaultCd;

    /// <summary>
    /// Injector pressure drop as a fraction of chamber pressure, ΔP_inj / P_c.
    /// Sized so that the selected orifice areas produce the required mass flow
    /// at this drop. Also the value fed to the chug-stability screening check,
    /// replacing the prior hardcoded nominal 20 %.
    /// Classical feasible band is [0.15, 0.25] (Huzel &amp; Huang §8.3); values
    /// outside cause the chug rating to degrade to Marginal or Fail, which
    /// the feasibility gate picks up through STABILITY_FAIL. Range clamp
    /// [0.05, 0.50] is enforced on consumption but not on this field itself
    /// so saved designs round-trip unchanged.
    /// </summary>
    [SaDesignVariable(index: 14, min: 0.13, max: 0.30, gate: SaGate.InjectorPatternPresent)]
    public double DeltaPInjFraction { get; init; } = 0.20;

    /// <summary>
    /// Impingement half-angle for unlike-doublet elements [degrees],
    /// 10-45° band. Consumed by
    /// <see cref="Elements.ImpingingDoubletElement.Size"/> via
    /// <see cref="Elements.SizingInputs.ImpingementHalfAngle_deg"/>;
    /// other element types ignore it. Default 20° matches the legacy
    /// element-class default so existing patterns reproduce the same
    /// notes verbatim.
    /// </summary>
    public double ImpingementHalfAngle_deg { get; init; } = 20.0;

    // ─────────────────────────────────────────────────────────────────
    //  Sprint 18 (2026-04-23) — Pintle-specific knobs
    //
    //  Pintle injectors are single-element (one pintle per chamber —
    //  the `Central` face layout handles placement) but have their own
    //  unique geometric knobs that the generic coax/doublet fields
    //  don't cover. Surfaced here rather than on `PintleElement`
    //  because the InjectorPattern is what the SA loop / UI / JSON
    //  persistence see. Element-ignored when ElementType != "Pintle".
    //
    //  Dressler stability bands (2000, SpaceX / TRW pintle heritage):
    //    • Blockage factor BL = N·d_sleeve / (π·D_pintle) ∈ [0.40, 0.85]
    //    • Total momentum ratio TMR = (ṁ_f·v_f)/(ṁ_ox·v_ox) ∈ [0.2, 4.0]
    //  Both are surfaced as feasibility gates (PINTLE_BLOCKAGE_OUT_OF_BAND,
    //  PINTLE_TMR_OUT_OF_BAND). Defaults below keep a nominal 500 N / 20 kN
    //  design inside both bands.
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Pintle-post diameter (mm). Scales with thrust; 6-20 mm covers the
    /// sub-kN to ~100 kN band. Consumed only when
    /// <see cref="ElementType"/> is <c>"Pintle"</c>.
    /// </summary>
    public double PintleDiameter_mm { get; init; } = 12.0;

    /// <summary>
    /// Number of axial sleeve holes around the pintle post. Together
    /// with <see cref="PintleDiameter_mm"/> sets the blockage factor
    /// via BL = N · d_sleeve / (π · D_pintle). 12–24 typical. Consumed
    /// only when <see cref="ElementType"/> is <c>"Pintle"</c>.
    /// </summary>
    public int PintleSleeveHoleCount { get; init; } = 18;

    /// <summary>
    /// Target blockage factor — 0.60 is Dressler's recommended centre
    /// of the stable-combustion band [0.40, 0.85]. The sizing routine
    /// doesn't currently use this target to iterate; it's surfaced so
    /// the UI can display it and future iterative auto-sizing can
    /// consume it. Consumed only when <see cref="ElementType"/> is
    /// <c>"Pintle"</c>.
    /// </summary>
    public double PintleBlockageFractionTarget { get; init; } = 0.60;

    /// <summary>
    /// Face layout strategy. Default
    /// <see cref="InjectorFaceLayout.Circular"/> reproduces the
    /// legacy single-pitch-circle behaviour so every existing design
    /// round-trips bit-identical. AutoSeeder emits Hexagonal for
    /// Coax / Showerhead, AnnularRows for Swirl, Central for Pintle,
    /// Circular for ImpingingDoublet.
    /// </summary>
    public InjectorFaceLayout FaceLayout { get; init; } = InjectorFaceLayout.Circular;

    /// <summary>
    /// The resolved element instance (derived from ElementType string).
    /// Not serialised — use ElementType for persistence.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IInjectorElement Element => InjectorElementFactory.Create(ElementType);

    // ─────────────────────────────────────────────────────────────────
    //  Factory helpers
    // ─────────────────────────────────────────────────────────────────

    public static InjectorPattern DefaultCoax(int elementCount = 20) => new()
    {
        ElementType = "Coax",
        ElementCount = elementCount,
        OuterRowFilmFraction = 0.05,
    };

    public static InjectorPattern DefaultImpinging(int elementCount = 20) => new()
    {
        ElementType = "ImpingingDoublet",
        ElementCount = elementCount,
        OuterRowFilmFraction = 0.04,
    };

    /// <summary>
    /// Sprint 18 (2026-04-23): default pintle pattern. Pintles are
    /// single-element (one pintle per chamber), so <see cref="ElementCount"/>
    /// is always 1 regardless of the caller's argument. Face layout
    /// defaults to <see cref="InjectorFaceLayout.Central"/>. Geometry
    /// defaults (12 mm post, 18 sleeve holes, 0.60 blockage target) match
    /// a ~20 kN LOX/CH4 design in the Dressler stable-combustion band.
    /// </summary>
    public static InjectorPattern DefaultPintle() => new()
    {
        ElementType                  = "Pintle",
        ElementCount                 = 1,
        FaceLayout                   = InjectorFaceLayout.Central,
        OuterRowFilmFraction         = 0.05,
        PintleDiameter_mm            = 12.0,
        PintleSleeveHoleCount        = 18,
        PintleBlockageFractionTarget = 0.60,
    };

    // ─────────────────────────────────────────────────────────────────
    //  Sizing
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Size all elements for the given total mass flows and pressure drop.
    /// Per-element flows = total / ElementCount.
    /// </summary>
    public PatternSizingResult SizePattern(
        double totalOxFlow_kgs,
        double totalFuelFlow_kgs,
        double deltaPInj_Pa,
        double oxDensity_kgm3,
        double fuelDensity_kgm3)
    {
        int n = System.Math.Max(ElementCount, 1);
        double filmFuel = System.Math.Clamp(OuterRowFilmFraction, 0.0, 0.30);
        double netFuel = totalFuelFlow_kgs * (1.0 - filmFuel);

        var inp = new SizingInputs(
            DeltaPInj_Pa:           deltaPInj_Pa,
            OxDensity_kgm3:         oxDensity_kgm3,
            FuelDensity_kgm3:       fuelDensity_kgm3,
            OxFlowPerElement_kgs:   totalOxFlow_kgs / n,
            FuelFlowPerElement_kgs: netFuel / n,
            CdOx:   CdOx,
            CdFuel: CdFuel,
            ImpingementHalfAngle_deg:      ImpingementHalfAngle_deg,
            // Sprint 18: thread Pintle knobs through. Non-pintle elements
            // ignore these fields; Pintle reads them in Element.Size().
            PintleDiameter_mm:             PintleDiameter_mm,
            PintleSleeveHoleCount:         PintleSleeveHoleCount,
            PintleBlockageFractionTarget:  PintleBlockageFractionTarget);

        var perElem = Element.Size(inp);

        double totalOxArea   = perElem.OxOrificeArea_mm2   * n;
        double totalFuelArea = perElem.FuelOrificeArea_mm2 * n;

        // Cross-check: reconstruct predicted total mass flow and compare.
        double predOx   = n * inp.OxDensity_kgm3   * OrificeModel.OrificeArea_m2(
            inp.OxFlowPerElement_kgs,   deltaPInj_Pa, oxDensity_kgm3,   CdOx)
            * CdOx * System.Math.Sqrt(2.0 * deltaPInj_Pa / oxDensity_kgm3);
        double predFuel = n * inp.FuelDensity_kgm3  * OrificeModel.OrificeArea_m2(
            inp.FuelFlowPerElement_kgs, deltaPInj_Pa, fuelDensity_kgm3, CdFuel)
            * CdFuel * System.Math.Sqrt(2.0 * deltaPInj_Pa / fuelDensity_kgm3);

        double targetTotal = totalOxFlow_kgs + netFuel;
        double predTotal   = predOx + predFuel;
        double check = predTotal / System.Math.Max(targetTotal, 1e-12);

        var warnings = new System.Collections.Generic.List<string>();
        foreach (var note in perElem.Notes) warnings.Add(note);
        if (!Element.IsImplemented)
            warnings.Add($"Element type '{ElementType}' is a stub — no sizing performed.");
        if (System.Math.Abs(check - 1.0) > 0.02)
            warnings.Add($"Flow split check = {check:F3} (expect 1.000 ± 0.02).");

        return new PatternSizingResult(
            ElementCount:     n,
            PerElementResult: perElem,
            TotalOxArea_mm2:  totalOxArea,
            TotalFuelArea_mm2: totalFuelArea,
            FlowSplitCheck:   check,
            Warnings: warnings.ToArray());
    }
}
