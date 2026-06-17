// BuildSheet.cs — Test-stand operator-facing "what do I order + what do I
// torque to what" Markdown artifact. Aggregates the existing flange / port /
// sensor-boss / umbilical preset data into a single reviewable build sheet.
// Voxel geometry of test-stand interfaces is out of scope for this artifact;
// the build sheet describes what's already on the design.

using System.Globalization;
using System.Text;
using Voxelforge.Combustion;
using Voxelforge.Geometry;
using Voxelforge.HeatTransfer;
using Voxelforge.Manufacturing;
using Voxelforge.Optimization;

namespace Voxelforge.IO;

public static class BuildSheet
{
    public const string ReportSchemaVersion = "v1.0 (2026-04-27)";

    public static string BuildMarkdown(OperatingConditions cond, RegenChamberDesign design)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();

        sb.AppendLine("# Test-Stand Build Sheet");
        sb.AppendLine();
        sb.AppendLine($"**Generated:** {DateTime.Now.ToString("yyyy-MM-dd HH:mm", ci)} (local)  ");
        sb.AppendLine($"**Schema:** {ReportSchemaVersion}");
        sb.AppendLine();
        sb.AppendLine("> **Preliminary — verify every part number, torque, and seal spec against your " +
                      "shop's certified data sheet before pulling parts.** This artifact aggregates the " +
                      "categorical selections on the design record into a single shopping list. It is " +
                      "not a substitute for a vendor-issued installation drawing or a torque-control " +
                      "procedure cleared by the test conductor.");
        sb.AppendLine();

        sb.AppendLine("## Engine summary");
        sb.AppendLine();
        var wall = WallMaterials.All[
            Math.Clamp(cond.WallMaterialIndex, 0, WallMaterials.All.Length - 1)];
        sb.AppendLine("| Quantity | Value |");
        sb.AppendLine("| --- | --- |");
        sb.AppendLine($"| Propellants | {cond.PropellantPair} |");
        sb.AppendLine($"| Thrust | {cond.Thrust_N:F0} N ({cond.Thrust_N / 4.448:F0} lbf) |");
        sb.AppendLine($"| Chamber pressure (MEOP) | {cond.ChamberPressure_Pa / 1e6:F2} MPa ({cond.ChamberPressure_Pa / 6894.76:F0} psia) |");
        sb.AppendLine($"| Mixture ratio (O/F) | {cond.MixtureRatio:F2} |");
        sb.AppendLine($"| Engine cycle | {cond.EngineCycle} |");
        sb.AppendLine($"| Wall material | {wall.Name} |");
        sb.AppendLine();

        sb.AppendLine("## Thrust-takeout flange (nozzle-exit mounting)");
        sb.AppendLine();
        var flange = MountingFlangePresets.SpecFor(design.MountingFlangeStandard);
        sb.AppendLine("| Quantity | Value |");
        sb.AppendLine("| --- | --- |");
        sb.AppendLine($"| Pattern | {flange.DisplayName} |");
        sb.AppendLine($"| Bolt count | {flange.BoltCount} |");
        sb.AppendLine($"| Bolt diameter | M{flange.BoltDiameter_mm:F0} (Ø{flange.BoltDiameter_mm:F1} mm) |");
        sb.AppendLine($"| Bolt-circle inset from flange OD | {flange.BoltCircleInset_mm:F1} mm |");
        sb.AppendLine($"| Flange radial margin (over jacket OD) | {flange.FlangeMarginRadius_mm:F1} mm |");
        sb.AppendLine($"| Pattern start angle | {flange.StartAngle_rad * 180.0 / Math.PI:F1}° |");
        sb.AppendLine($"| Recommended grade (steel) | ISO 898-1 property class 8.8 |");
        sb.AppendLine($"| Recommended grade (stainless) | ISO 3506-1 A2-70 or A4-70 (cryo-compatible) |");
        sb.AppendLine();

        sb.AppendLine("### Recommended bolt-up torque (dry, lightly oiled threads)");
        sb.AppendLine();
        sb.AppendLine("Starting recommendation per ISO 898-1 / ISO 3506-1 — adjust for the actual " +
                      "lubricant (moly grease typically halves these), thread-locker (Loctite 243 " +
                      "adds ~10–15 %), and any qualified torque-control procedure for your facility.");
        sb.AppendLine();
        sb.AppendLine("| Bolt size | Class 8.8 steel | A2-70 stainless |");
        sb.AppendLine("| --- | ---: | ---: |");
        var torque = FastenerTorqueTable.Lookup(flange.BoltDiameter_mm);
        sb.AppendLine($"| M{flange.BoltDiameter_mm:F0} | {torque.Class88_Nm:F1} N·m ({torque.Class88_Nm * 0.7376:F1} ft·lb) | {torque.Stainless_Nm:F1} N·m ({torque.Stainless_Nm * 0.7376:F1} ft·lb) |");
        sb.AppendLine();
        sb.AppendLine($"**Total fasteners required for this flange:** {flange.BoltCount}× M{flange.BoltDiameter_mm:F0}, " +
                      $"plus matching washers and (if cryogenic service) Belleville stacks for thermal-contraction preload retention.");
        sb.AppendLine();

        // Hot-fire readiness Item 6 / OOB-260 (2026-04-30): the engine-side
        // mounting flange is the chamber's interface; when an integrated
        // thrust-takeout adapter is also enabled, the saved STL ships with
        // a structural extension whose own bottom bolt circle bolts to the
        // load cell. Document the second bolt circle so the test-stand
        // operator orders the right hardware AND uses the right torque.
        if (design.IncludeMountingFlange && design.IncludeThrustTakeoutAdapter)
        {
            var standMount = MountingFlangePresets.SpecFor(design.ThrustTakeoutMountStandard);
            var standTorque = FastenerTorqueTable.Lookup(standMount.BoltDiameter_mm);
            sb.AppendLine("## Test-stand thrust-takeout adapter");
            sb.AppendLine();
            sb.AppendLine("This design ships an integrated structural adapter aft of the mounting " +
                          "flange. The adapter is printed as part of the chamber STL; the flange " +
                          "above is its **engine-side** interface. The figures below describe its " +
                          "**stand-side** interface, which bolts to the load cell.");
            sb.AppendLine();
            sb.AppendLine("| Quantity | Value |");
            sb.AppendLine("| --- | --- |");
            sb.AppendLine($"| Adapter height | {design.ThrustTakeoutAdapterHeight_mm:F1} mm |");
            string odLabel = design.ThrustTakeoutOuterDiameter_mm > 0
                ? $"{design.ThrustTakeoutOuterDiameter_mm:F1} mm"
                : "match mounting-flange OD";
            sb.AppendLine($"| Adapter outer diameter | {odLabel} |");
            sb.AppendLine($"| Stand-side pattern | {standMount.DisplayName} |");
            sb.AppendLine($"| Stand-side bolt count | {standMount.BoltCount} |");
            sb.AppendLine($"| Stand-side bolt diameter | M{standMount.BoltDiameter_mm:F0} (Ø{standMount.BoltDiameter_mm:F1} mm) |");
            sb.AppendLine($"| Stand-side bolt-circle inset | {standMount.BoltCircleInset_mm:F1} mm |");
            if (design.ThrustTakeoutUmbilicalPassThroughCount > 0)
            {
                sb.AppendLine($"| Umbilical pass-throughs | {design.ThrustTakeoutUmbilicalPassThroughCount}× Ø{design.ThrustTakeoutUmbilicalPassThroughDiameter_mm:F1} mm (radial) |");
            }
            else
            {
                sb.AppendLine("| Umbilical pass-throughs | none |");
            }
            sb.AppendLine();
            sb.AppendLine($"**Stand-side fasteners:** {standMount.BoltCount}× M{standMount.BoltDiameter_mm:F0} at " +
                          $"{standTorque.Class88_Nm:F1} N·m (Class 8.8 steel) / " +
                          $"{standTorque.Stainless_Nm:F1} N·m (A2-70 stainless).");
            sb.AppendLine();
        }

        // OOB-6 / Sprint B-3 (#200, 2026-04-30): document the acoustic-damper
        // array when present. Section emits only when DamperType ≠ None
        // (legacy behaviour bit-identical for designs without dampers). The
        // f₀ + Δζ figures come from the test-stand operator's perspective:
        // they're inspecting the print to see whether the chamber was
        // expected to need active damping, what mode it tunes against,
        // and how strong the damping should be.
        if (design.DamperType != Combustion.Stability.AcousticDamperType.None
            && design.DamperCount > 0)
        {
            sb.AppendLine("## Acoustic dampers");
            sb.AppendLine();
            sb.AppendLine("| Quantity | Value |");
            sb.AppendLine("| --- | --- |");
            sb.AppendLine($"| Damper family | {design.DamperType} |");
            sb.AppendLine($"| Resonator count | {design.DamperCount} (distributed at {360.0 / design.DamperCount:F1}° pitch) |");
            if (design.DamperType == Combustion.Stability.AcousticDamperType.Helmholtz)
            {
                double neckDia = 2.0 * Math.Sqrt(design.HelmholtzNeckArea_mm2 / Math.PI);
                sb.AppendLine($"| Neck area | {design.HelmholtzNeckArea_mm2:F1} mm² (Ø{neckDia:F2} mm) |");
                sb.AppendLine($"| Neck length | {design.HelmholtzNeckLength_mm:F2} mm |");
                sb.AppendLine($"| Cavity volume | {design.HelmholtzCavityVolume_mm3:F0} mm³ |");
            }
            else if (design.DamperType == Combustion.Stability.AcousticDamperType.QuarterWave)
            {
                sb.AppendLine($"| Cavity length | {design.QuarterWaveLength_mm:F2} mm |");
                sb.AppendLine($"| Cavity diameter | {design.QuarterWaveDiameter_mm:F2} mm |");
            }
            sb.AppendLine();
            sb.AppendLine("**Note:** Resonance frequency and per-mode damping ratio (Δζ) are " +
                          "computed by `AcousticDamper.Evaluate` and surfaced on " +
                          "`StabilityReport.AcousticDamper`. Damper-tuning model is empirical " +
                          "(Harrje & Reardon §8 Q ≈ 15 anchor) — treat numbers as advisory.");
            sb.AppendLine();
        }

        // Sprint C (#350): combined-load structural margin summary — emitted
        // only when TVC gimballing is configured. The section is advisory;
        // the pass/fail result is in the SafetyReport (which has access to
        // the full StructuralSummary). BuildSheet only has OperatingConditions
        // + RegenChamberDesign, so it surfaces the configuration, not the margin.
        if (cond.GimbalOffset_mm > 0)
        {
            sb.AppendLine("## Combined-load structural margin");
            sb.AppendLine();
            sb.AppendLine("TVC gimballing is active. The `COMBINED_AXIAL_BENDING_INSUFFICIENT` " +
                          "feasibility gate checks peak von Mises (hoop + axial membrane + " +
                          "bending extreme fiber) against σ_y / 1.5 at every axial station " +
                          "(Hibbeler §8.4, biaxial no-shear combined-load case).");
            sb.AppendLine();
            sb.AppendLine("| Quantity | Value |");
            sb.AppendLine("| --- | --- |");
            sb.AppendLine($"| Gimbal offset | {cond.GimbalOffset_mm:F1} mm |");
            sb.AppendLine($"| Combined-load SF requirement | 1.5× |");
            sb.AppendLine();
        }

        if (design.EnableTranspirationCooling)
        {
            sb.AppendLine("## Transpiration cooling");
            sb.AppendLine();
            sb.AppendLine("Transpiration cooling is active. Coolant is bled through the porous " +
                          "LPBF chamber wall into the boundary layer via the Eckert-Livingood " +
                          "effusion model (Sutton §4.3).");
            sb.AppendLine();
            sb.AppendLine("| Quantity | Value |");
            sb.AppendLine("| --- | --- |");
            sb.AppendLine($"| Bleed fraction | {design.TranspirationBleedFraction:P1} of coolant mass flow |");
            sb.AppendLine($"| Efficiency η_t | {design.TranspirationEfficiency:F2} |");
            sb.AppendLine();
            sb.AppendLine("**LPBF microporosity target:** 10–30 µm (CuCrZr / GRCop-42 literature " +
                          "anchor, Sutton §4.3). Verify pore interconnection via CT scan before " +
                          "first hot-fire.");
            sb.AppendLine();
        }

        if (design.ChannelTopology == ChannelTopology.AblativeThroat
            && design.AblativeMaterial != Manufacturing.AblativeMaterial.None)
        {
            sb.AppendLine("## Ablative + regen hybrid throat");
            sb.AppendLine();
            sb.AppendLine("Regen channels cover the chamber and divergent section. " +
                          "An ablative liner occupies the throat band.");
            sb.AppendLine();
            sb.AppendLine("| Parameter | Value |");
            sb.AppendLine("| --- | --- |");
            sb.AppendLine($"| Ablative material | {design.AblativeMaterial} |");
            sb.AppendLine($"| Initial liner thickness | {design.AblativeThickness_mm:F1} mm |");
            sb.AppendLine($"| Burn duration | {design.AblativeBurnDuration_s:F1} s |");
            sb.AppendLine($"| Safety factor | {design.AblativeSafetyFactor:F2}× (recession + char depth vs. thickness) |");
            sb.AppendLine($"| Zone start (axial frac.) | {design.AblativeZoneStart_frac:P0} |");
            sb.AppendLine($"| Zone end (axial frac.) | {design.AblativeZoneEnd_frac:P0} |");
            sb.AppendLine();
            sb.AppendLine("> **Note:** The ablative liner must be bonded or press-fitted into the " +
                          "throat band machined into the LPBF structure. Verify bonding procedure " +
                          "against the material spec (Sutton 9e §16.3). " +
                          "`ABLATIVE_THROAT_RECESSION_EXCEEDS_BUDGET` fires if predicted burnthrough " +
                          "precedes end-of-burn.");
            sb.AppendLine();
        }

        sb.AppendLine("## Ground-side umbilical / quick-disconnect");
        sb.AppendLine();
        if (cond.UmbilicalStandard == UmbilicalStandard.None)
        {
            sb.AppendLine("**No umbilical selected** — the design assumes the propellant lines connect " +
                          "directly through the test-stand plumbing without a dis/reconnect interface. " +
                          "Set `OperatingConditions.UmbilicalStandard` if a QD is required.");
        }
        else
        {
            var umb = UmbilicalStandards.SpecFor(cond.UmbilicalStandard);
            sb.AppendLine("| Quantity | Value |");
            sb.AppendLine("| --- | --- |");
            sb.AppendLine($"| Standard | {umb.DisplayName} |");
            sb.AppendLine($"| Face OD | {umb.FaceOuterDiameter_mm:F1} mm |");
            sb.AppendLine($"| Bore ID | {umb.BoreInnerDiameter_mm:F1} mm |");
            sb.AppendLine($"| Seal-groove depth | {umb.SealGrooveDepth_mm:F2} mm |");
            sb.AppendLine($"| Loss coefficient K | {umb.LossCoefficientK:F2} |");
            sb.AppendLine();
            sb.AppendLine("**Seals:** match the seal-groove geometry above. For cryogenic service, " +
                          "use spring-energised PTFE or polyimide-jacketed seals (e.g. Bal Seal " +
                          "FlexiSeal series); for ambient propellant, fluoroelastomer (Viton) is " +
                          "adequate to ≈ −40 °C.");
        }
        sb.AppendLine();

        sb.AppendLine("## Threaded ports on the chamber");
        sb.AppendLine();
        sb.AppendLine("| Port | Standard | Major Ø | Pitch | Thread length | Boss Ø |");
        sb.AppendLine("| --- | --- | ---: | ---: | ---: | ---: |");
        AppendPortRow(sb, "Coolant inlet/outlet", design.CoolantPortStandard);
        AppendPortRow(sb, "Propellant inlet", design.PropellantPortStandard);
        sb.AppendLine();

        sb.AppendLine("## Instrumentation bosses");
        sb.AppendLine();
        if (design.SensorBosses.Count == 0)
        {
            sb.AppendLine("**No instrumentation bosses on this design.** Add `SensorBoss` entries " +
                          "to `RegenChamberDesign.SensorBosses` for any pressure transducers, " +
                          "thermocouples, or static taps required by the test plan.");
        }
        else
        {
            sb.AppendLine($"{design.SensorBosses.Count} boss(es) on this design.");
            sb.AppendLine();
            sb.AppendLine("| # | Type | Axial fraction (0=injector face → 1=exit) | Azimuth | Bore Ø | Boss OD |");
            sb.AppendLine("| ---: | --- | ---: | ---: | ---: | ---: |");
            for (int i = 0; i < design.SensorBosses.Count; i++)
            {
                var b = design.SensorBosses[i];
                var spec = SensorBossPresets.SpecFor(b.Type);
                sb.AppendLine($"| {i + 1} | {spec.DisplayName} | {b.AxialFraction:F2} | {b.AzimuthDeg:F0}° | " +
                              $"{spec.BoreDiameter_mm:F1} mm | {spec.BossOuterDiameter_mm:F1} mm |");
            }
        }
        sb.AppendLine();

        sb.AppendLine("## Feed lines");
        sb.AppendLine();
        bool isCryo = cond.PropellantPair is PropellantPair.LOX_CH4 or PropellantPair.LOX_H2;
        sb.AppendLine("| Quantity | Value |");
        sb.AppendLine("| --- | --- |");
        sb.AppendLine($"| Straight-pipe length (per side) | {cond.FeedLineLength_m:F2} m |");
        sb.AppendLine($"| Inner diameter | {cond.FeedLineDiameter_mm:F1} mm |");
        sb.AppendLine($"| Cryogenic service | {(isCryo ? "YES — flex lines must be vacuum-jacketed or perlite-insulated" : "no — ambient flexible hose acceptable")} |");
        sb.AppendLine();
        if (isCryo)
        {
            sb.AppendLine("**Cryo-line callouts:**");
            sb.AppendLine();
            sb.AppendLine("- Specify vacuum-jacketed (VJ) flex hose for any line carrying liquid " +
                          "propellant (e.g. Cryofab CryoFlex or equivalent). Bend radius typically " +
                          "10× hose OD minimum.");
            sb.AppendLine("- Allow for thermal contraction over the line length: ≈ 0.3 % for stainless " +
                          $"steel from 293 K to LOX boiling (90 K) → {cond.FeedLineLength_m * 1000 * 0.003:F0} mm net contraction over {cond.FeedLineLength_m:F1} m.");
            sb.AppendLine("- Include a low-point drain valve and a relief valve sized for trapped-volume " +
                          "boil-off on every cryogenic line segment that can be isolated.");
            sb.AppendLine();
        }

        sb.AppendLine("## Pre-fire checklist");
        sb.AppendLine();
        sb.AppendLine("- [ ] All flange bolts torqued to spec, verified with calibrated torque wrench.");
        sb.AppendLine("- [ ] Cold helium leak-down at MEOP passed (separate test — not covered by this build sheet).");
        sb.AppendLine("- [ ] Cold hydrostatic proof test passed — see the SafetyReport artifact for the proof-pressure check.");
        sb.AppendLine("- [ ] Instrumentation bosses populated with calibrated transducers / thermocouples; cabling routed and clamped.");
        sb.AppendLine("- [ ] Cryogenic flex lines installed, vacuum-jacket vacuum verified (where applicable).");
        sb.AppendLine("- [ ] Test-stand exclusion zone cleared per facility hazard analysis.");
        sb.AppendLine("- [ ] Abort sequence + valve fail-safe positions confirmed by test conductor.");
        sb.AppendLine();

        sb.AppendLine("## Limitations");
        sb.AppendLine();
        sb.AppendLine("This artifact:");
        sb.AppendLine();
        sb.AppendLine("- **Does not size the test-stand thrust takeout structure itself** — only the " +
                      "interface bolt pattern. Stand-side adapter plate, load-cell mounting, and pedestal " +
                      "structural design are out of scope.");
        sb.AppendLine("- **Does not specify part numbers** for any vendor-supplied component — only the " +
                      "interface dimensions and standards. Source actual parts from your qualified vendor list.");
        sb.AppendLine("- **Torque values above are starting recommendations only.** Use the qualified " +
                      "torque-control procedure for your facility, accounting for thread lubricant, " +
                      "thread-locker, and acceptable preload scatter.");
        sb.AppendLine("- **Cryo-line specs are general guidance.** Detailed flex-line selection depends " +
                      "on flow, pressure, vibration environment, and run length — confirm with the " +
                      "vendor's selection chart for your specific service.");

        return sb.ToString();
    }

    public static void SaveMarkdown(string path, OperatingConditions cond, RegenChamberDesign design)
    {
        File.WriteAllText(path, BuildMarkdown(cond, design));
    }

    private static void AppendPortRow(StringBuilder sb, string role, PortStandard std)
    {
        var spec = PortStandards.Get(std);
        if (std == PortStandard.Plain)
        {
            sb.AppendLine($"| {role} | {spec.Name} | — | — | — | — |");
            return;
        }
        sb.AppendLine($"| {role} | {spec.Name} | {spec.MajorDiaMM:F2} mm | {spec.PitchMM:F2} mm | {spec.ThreadLengthMM:F1} mm | {spec.BossDiaMM:F2} mm |");
    }
}

public readonly record struct FastenerTorqueRow(double BoltDiameter_mm, double Class88_Nm, double Stainless_Nm);

public static class FastenerTorqueTable
{
    private static readonly FastenerTorqueRow[] _rows = new[]
    {
        new FastenerTorqueRow(BoltDiameter_mm: 3,  Class88_Nm:  1.3, Stainless_Nm:  0.9),
        new FastenerTorqueRow(BoltDiameter_mm: 4,  Class88_Nm:  3.0, Stainless_Nm:  2.1),
        new FastenerTorqueRow(BoltDiameter_mm: 5,  Class88_Nm:  6.0, Stainless_Nm:  4.2),
        new FastenerTorqueRow(BoltDiameter_mm: 6,  Class88_Nm: 10.5, Stainless_Nm:  7.4),
        new FastenerTorqueRow(BoltDiameter_mm: 8,  Class88_Nm: 25.0, Stainless_Nm: 17.5),
        new FastenerTorqueRow(BoltDiameter_mm: 10, Class88_Nm: 50.0, Stainless_Nm: 36.0),
        new FastenerTorqueRow(BoltDiameter_mm: 12, Class88_Nm: 87.0, Stainless_Nm: 61.0),
    };

    public static FastenerTorqueRow Lookup(double boltDiameter_mm)
    {
        FastenerTorqueRow best = _rows[0];
        double bestDiff = Math.Abs(_rows[0].BoltDiameter_mm - boltDiameter_mm);
        for (int i = 1; i < _rows.Length; i++)
        {
            double diff = Math.Abs(_rows[i].BoltDiameter_mm - boltDiameter_mm);
            if (diff < bestDiff) { best = _rows[i]; bestDiff = diff; }
        }
        return best;
    }
}
