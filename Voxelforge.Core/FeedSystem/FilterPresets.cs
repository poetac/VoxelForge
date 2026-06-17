// FilterPresets.cs — Inline propellant filter / contamination ΔP
// budget for the feed-system stackup.
//
// Mirrors the established preset-library pattern (PortStandards,
// SensorBossPresets, MountingFlangePresets, UmbilicalStandards,
// IgniterPresets): an enum of named presets + a spec record + a
// lookup dictionary + helpers. Replaces a legacy scalar
// `OperatingConditions.FilterDeltaP_Pa` with a clean / dirty model
// so the stackup can answer the NESC-style "what happens when the
// filter loads up" question.
//
// MVP scope:
//   • Five entries cover the LRE feed-system filter range:
//       None              0 ΔP / 0 µm        — bypass
//       CoarseMesh_100um  ~30 kPa clean      — last-chance strainer
//       Standard_40um     ~100 kPa clean     — typical inline filter
//       Fine_25um         ~200 kPa clean     — injector-protection
//       UltraFine_10um    ~500 kPa clean     — critical cleanliness
//       Custom            user scalar        — back-compat passthrough
//   • Each preset carries a clean ΔP at rated flow + a "dirty"
//     multiplier representing end-of-life loading. Linear interp
//     between clean and dirty by `FilterContaminationFraction`
//     ∈ [0, 1] so the user can sweep loading without changing
//     hardware.
//   • The Custom preset reads the existing
//     `OperatingConditions.FilterDeltaP_Pa` field as the clean
//     value and applies a generic 3.0× dirty multiplier — this
//     preserves backward compat with v6 / v7 saved designs that
//     pre-date the preset library.
//
// Adding a preset: drop a new entry into the `All` dictionary
// below. The PressureStackup consumes the spec via
// `EffectiveDeltaP_Pa(...)` only; no other file needs to change.
//
// References:
//   NASA-STD-6016 §4.3 (cleanliness levels for propellant systems);
//   AIAA G-077 §7 (filter sizing for ground-fed liquid-rocket test
//   stands); Idelchik §8 (woven-mesh / sintered-disc pressure-loss
//   coefficients).

namespace Voxelforge.FeedSystem;

public enum FilterStandard
{
    /// <summary>No filter — stackup charges 0 ΔP for this segment.</summary>
    None = 0,
    /// <summary>Coarse 100 µm mesh — last-chance strainer ahead of the main valve.</summary>
    CoarseMesh_100um,
    /// <summary>Standard 40 µm inline propellant filter — typical pressure-fed test stand.</summary>
    Standard_40um,
    /// <summary>Fine 25 µm injector-protection filter.</summary>
    Fine_25um,
    /// <summary>Ultra-fine 10 µm filter for critical-cleanliness applications.</summary>
    UltraFine_10um,
    /// <summary>Use the user-supplied <c>FilterDeltaP_Pa</c> scalar; back-compat path.</summary>
    Custom,
}

/// <summary>
/// One filter preset. <see cref="NominalCleanDP_Pa"/> is the rated
/// pressure drop on a fresh element at the design mass flow;
/// <see cref="DirtyMultiplier"/> is the end-of-life multiplier
/// (typical 3-5× for woven-mesh / sintered-disc media).
/// <see cref="Rating_um"/> is the nominal absolute filtration rating
/// in micrometers (0 = no rating / bypass).
/// </summary>
public readonly record struct FilterSpec(
    FilterStandard Id,
    string DisplayName,
    double NominalCleanDP_Pa,
    double DirtyMultiplier,
    double Rating_um,
    string Notes);

public static class FilterPresets
{
    /// <summary>
    /// Generic dirty multiplier applied when <see cref="FilterStandard.Custom"/>
    /// is selected — 3.0× is a reasonable bound for woven-mesh elements at
    /// end-of-life. Users wanting a tighter / looser bound should switch to
    /// one of the named presets.
    /// </summary>
    public const double CustomDirtyMultiplier = 3.0;

    public static readonly System.Collections.Generic.Dictionary<FilterStandard, FilterSpec> All =
        new()
        {
            [FilterStandard.None] = new(
                FilterStandard.None, "(no filter)",
                NominalCleanDP_Pa: 0, DirtyMultiplier: 1.0, Rating_um: 0,
                Notes: "Stackup charges no ΔP for this segment."),

            [FilterStandard.CoarseMesh_100um] = new(
                FilterStandard.CoarseMesh_100um, "Coarse mesh 100 µm",
                NominalCleanDP_Pa: 30_000, DirtyMultiplier: 2.5, Rating_um: 100,
                Notes: "Last-chance strainer; tolerates heavy loading before measurable ΔP rise."),

            [FilterStandard.Standard_40um] = new(
                FilterStandard.Standard_40um, "Standard inline 40 µm",
                NominalCleanDP_Pa: 100_000, DirtyMultiplier: 3.5, Rating_um: 40,
                Notes: "Typical pressure-fed propellant filter; replace per cycle count or ΔP trend."),

            [FilterStandard.Fine_25um] = new(
                FilterStandard.Fine_25um, "Fine injector-protection 25 µm",
                NominalCleanDP_Pa: 200_000, DirtyMultiplier: 4.0, Rating_um: 25,
                Notes: "Injector-protection grade; loading tracked by stand DAQ to schedule swap-outs."),

            [FilterStandard.UltraFine_10um] = new(
                FilterStandard.UltraFine_10um, "Ultra-fine 10 µm",
                NominalCleanDP_Pa: 500_000, DirtyMultiplier: 5.0, Rating_um: 10,
                Notes: "Critical-cleanliness applications (tight-tolerance injector elements / cryogenic film cooling)."),

            [FilterStandard.Custom] = new(
                FilterStandard.Custom, "Custom (user scalar)",
                NominalCleanDP_Pa: 0, DirtyMultiplier: CustomDirtyMultiplier, Rating_um: 0,
                Notes: "Reads OperatingConditions.FilterDeltaP_Pa as the clean value; applies a generic 3× dirty multiplier."),
        };

    public static FilterSpec SpecFor(FilterStandard s) => All[s];

    /// <summary>
    /// Effective filter ΔP at the current contamination state.
    /// <paramref name="customCleanDP_Pa"/> is consumed only when
    /// <paramref name="standard"/> is <see cref="FilterStandard.Custom"/>;
    /// otherwise the preset's tabulated value wins.
    /// <paramref name="contaminationFraction"/> is clamped to [0, 1] —
    /// 0 = clean element, 1 = end-of-life. Loading is treated as
    /// linear in the multiplier (a deliberate simplification; real
    /// woven-mesh media exhibit an exponential rise near terminal
    /// ΔP, but the linear band is conservative within the 0-90 %
    /// service window).
    /// </summary>
    public static double EffectiveDeltaP_Pa(
        FilterStandard standard,
        double customCleanDP_Pa,
        double contaminationFraction)
    {
        var spec = All[standard];
        double frac = System.Math.Clamp(contaminationFraction, 0.0, 1.0);
        double clean = standard == FilterStandard.Custom
            ? System.Math.Max(customCleanDP_Pa, 0.0)
            : spec.NominalCleanDP_Pa;
        double mult = 1.0 + frac * (spec.DirtyMultiplier - 1.0);
        return clean * mult;
    }
}
