// StructuralCheck.cs — First-order hoop and thermal-stress margins.
//
// Thin-wall hoop stress (inner wall, treating coolant jacket as pressure vessel):
//     σ_hoop = P · r / t
//
// Through-wall thermal stress for a plate with fixed in-plane ends and a
// temperature gradient ΔT across thickness t:
//     σ_thermal ≈ α · E · ΔT / (2 · (1 − ν))            (Timoshenko)
// where ΔT = T_wg − T_wc.  The factor of 2 is because the linear through-
// thickness gradient puts half the total ΔT in compression on the hot
// side and half in tension on the cold side; peak magnitude is α E ΔT / (2(1−ν)).
//
// Combined stress uses von Mises for axial+hoop, then adds thermal as a
// separate principal (conservative).  Real analysis needs proper FEA with
// temperature-dependent material cards and low-cycle fatigue curves; this
// is a go/no-go gate for optimization, not a design certification.

using Voxelforge.HeatTransfer;

namespace Voxelforge.Structure;

public readonly record struct StationStressResult(
    int Index,
    double X_mm,
    double HoopStress_MPa,
    double ThermalStress_MPa,
    double CombinedVonMises_MPa,
    double YieldAtTemp_MPa,
    double SafetyFactor,
    // Sprint C (#350): combined axial-bending VM per Hibbeler §8.4.
    // 0.0 when GimbalOffset_mm = 0 (gate self-suppressed, backward-compat).
    double AxialBendingVM_MPa = 0.0);

public sealed record StructuralSummary(
    StationStressResult[] Stations,
    double PeakHoop_MPa,
    double PeakThermal_MPa,
    double PeakCombined_MPa,
    double MinSafetyFactor,
    int PeakStationIndex,
    bool YieldExceeded,
    // Z3-M2 (2026-04-29): bimetallic bond-zone shear from CTE mismatch.
    // τ_bond = ΔT·|α_liner−α_jacket|·E_eff (Hibbeler §8.4).
    // 0.0 for single-material walls (no jacketMaterial supplied).
    double BondZoneShearStress_MPa = 0.0,
    // τ_bond / (σ_y_min·0.5). Advisory gate fires when ratio > 1.
    double BondZoneShearRatio = 0.0,
    // Sprint C (#350): peak combined axial-bending VM across all stations.
    // 0.0 when GimbalOffset_mm = 0 (no gimballing configured).
    double PeakAxialBendingVM_MPa = 0.0,
    // σ_y at the peak-axial-bending station (MPa). Used by the gate to
    // compare peak VM against σ_y / 1.5 without re-iterating stations.
    double PeakAxialBendingYield_MPa = 0.0);

public static class StructuralCheck
{
    // Thin-wall inner wall Poisson's ratio (approximate, Cu alloys ≈ 0.33, Ni alloys ≈ 0.30)
    private const double DefaultPoisson = 0.32;

    /// <summary>
    /// Track B (2026-04-27): build a per-station gas-side wall thickness
    /// profile from a (chamberOverride, throatOverride, exitOverride)
    /// triple plus a uniform-baseline fallback. Each anchor falls back to
    /// <paramref name="baseline_mm"/> when its override value is &lt;= 0.
    /// Profile interpolates linearly in station-index space between the
    /// three anchors with the throat located at <paramref name="throatIdx"/>.
    /// When all three overrides are 0 the result is a uniform array of
    /// <paramref name="baseline_mm"/>, preserving pre-Track-B behavior.
    /// </summary>
    public static double[] BuildGasSideWallProfile_mm(
        int stationCount,
        int throatIdx,
        double baseline_mm,
        double chamberOverride_mm,
        double throatOverride_mm,
        double exitOverride_mm)
    {
        if (stationCount <= 0) return Array.Empty<double>();
        int throat = Math.Clamp(throatIdx, 0, stationCount - 1);
        double chamber = chamberOverride_mm > 0 ? chamberOverride_mm : baseline_mm;
        double throat_t = throatOverride_mm > 0 ? throatOverride_mm : baseline_mm;
        double exit = exitOverride_mm > 0 ? exitOverride_mm : baseline_mm;

        var profile = new double[stationCount];
        for (int i = 0; i < stationCount; i++)
        {
            if (i < throat)
            {
                // Strict < throat so the throat station itself takes the
                // throat-side branch and edge-cases like throatIdx=0 still
                // resolve to throat_t at i=0.
                double frac = throat > 0 ? (double)i / throat : 0.0;
                profile[i] = chamber + frac * (throat_t - chamber);
            }
            else
            {
                double denom = Math.Max(stationCount - 1 - throat, 1);
                double frac = (double)(i - throat) / denom;
                profile[i] = throat_t + frac * (exit - throat_t);
            }
        }
        return profile;
    }

    /// <summary>
    /// Locate the throat station as the minimum-R station in the solver
    /// outputs (matches the convention used elsewhere in the pipeline —
    /// e.g. ProofTestAnalysis preserves R_mm when synthesising cold
    /// stations).
    /// </summary>
    public static int FindThroatStationIndex(RegenSolverOutputs solver)
    {
        if (solver.Stations.Length == 0) return 0;
        int idx = 0;
        double minR = solver.Stations[0].R_mm;
        for (int i = 1; i < solver.Stations.Length; i++)
        {
            if (solver.Stations[i].R_mm < minR)
            {
                minR = solver.Stations[i].R_mm;
                idx = i;
            }
        }
        return idx;
    }

    /// <summary>
    /// Z1 hot-fix / Track B closed-loop (2026-04-28): contour-only overload.
    /// Locates the throat station BEFORE the thermal solver runs so callers
    /// can build a per-station <see cref="BuildGasSideWallProfile_mm"/>
    /// wallProfile and feed it INTO
    /// <see cref="HeatTransfer.RegenSolverInputs.GasSideWallProfile_mm"/>.
    /// Identical convention to the solver-output overload (minimum-R
    /// station) — the thermal march doesn't change station radii.
    /// </summary>
    public static int FindThroatStationIndex(Chamber.ChamberContour contour)
    {
        if (contour.Stations.Length == 0) return 0;
        int idx = 0;
        double minR = contour.Stations[0].R_mm;
        for (int i = 1; i < contour.Stations.Length; i++)
        {
            if (contour.Stations[i].R_mm < minR)
            {
                minR = contour.Stations[i].R_mm;
                idx = i;
            }
        }
        return idx;
    }

    /// <summary>
    /// Evaluate structural margins per station using results from the regen solver.
    /// </summary>
    /// <param name="solver">Regen cooling solver output.</param>
    /// <param name="wall">Wall material (T-dependent yield/E/α).</param>
    /// <param name="gasSideWallThickness_mm">Inner wall thickness between gas and coolant.</param>
    /// <param name="chamberPressure_Pa">Stagnation P_c.</param>
    /// <param name="outerJacketThickness_mm">
    /// Sprint feasibility-audit-G' (2026-04-27): outer jacket thickness in mm.
    /// When &gt; 0, the hoop stress is computed against the SUM of gas-side wall
    /// + outer jacket wall, treating both as parallel hoop-load-bearing
    /// cylinders (real LRE bimetallic chambers — Cu liner + Inconel jacket —
    /// distribute hoop load across both walls). Default 0 preserves the
    /// pre-G' single-wall behavior so test callers using the old signature
    /// see no change.
    /// </param>
    /// <param name="gasGamma">
    /// Hot-gas specific heat ratio for per-station static-pressure derivation
    /// via isentropic flow (P_static = P_c · (1 + (γ-1)/2 · M²)^(-γ/(γ-1))).
    /// When &gt; 0, the formula uses per-station local gas P (which drops
    /// sharply through the throat-to-exit expansion) instead of the constant
    /// chamber Pc as the gas-side pressure. Pass 0.0 for cold / proof-test
    /// callers where no hot gas flows (activates the legacy constant-Pc path).
    /// </param>
    public static StructuralSummary Evaluate(
        RegenSolverOutputs solver,
        WallMaterial wall,
        double gasSideWallThickness_mm,
        double chamberPressure_Pa,
        double gasGamma,
        double outerJacketThickness_mm = 0.0,
        double[]? gasSideWallProfile_mm = null,
        // A1-follow-on (2026-04-28): outer jacket material for composite-
        // yield accounting on bimetallic walls. When non-null AND
        // outerJacketThickness_mm > 0, the effective σ_y at each station
        // is the load-weighted (= thickness-weighted) blend of the inner
        // liner's yield at the gas-side surface T (Z2.6 conservative
        // choice, hottest face for the liner) and the jacket's yield at
        // the local coolant bulk T (jacket is wetted by the coolant
        // channel in a regen design). Default null preserves pre-merge
        // single-material yield behaviour bit-identically.
        WallMaterial? jacketMaterial = null,
        // Sprint C (#350): distance (m) from TVC gimbal attach to the throat
        // section. 0 = no gimballing; gate self-suppresses and outputs are
        // bit-identical to pre-Sprint-C. Non-zero activates σ_axial_membrane
        // + σ_bending + combined Hibbeler §8.4 VM at every station.
        double gimbalOffset_m = 0.0)
    {
        var stations = new StationStressResult[solver.Stations.Length];
        double peakHoop = 0, peakThermal = 0, peakCombined = 0;
        double minSF = double.MaxValue;
        double minSigmaY_MPa = double.MaxValue;
        double peakBondShear_MPa = 0.0;
        int peakIdx = 0;
        bool yielded = false;

        // Sprint G': effective hoop-load thickness = inner wall + outer jacket.
        // For multi-wall structural cylinders both walls share the load
        // (textbook composite cylinder analysis at the same circumferential
        // strain). Backwards-compat: outerJacketThickness_mm default 0 means
        // legacy single-wall calculation.
        //
        // Track B (2026-04-27): per-station gas-side wall thickness profile
        // overrides the uniform `gasSideWallThickness_mm` scalar when
        // provided. Lets the optimizer thicken the wall locally (typically
        // at the exit station for large-ε designs) without paying the
        // chamber/throat thermal penalty. Outer jacket remains uniform —
        // only the gas-side liner varies axially.
        double t_jacket_m = System.Math.Max(outerJacketThickness_mm, 0) * 1e-3;

        // Z3-M2: pre-compute CTE delta; non-zero only when jacketMaterial is
        // present, jacket thickness is positive, and the two materials have
        // meaningfully different CTEs.
        double deltaAlpha = jacketMaterial.HasValue && t_jacket_m > 0
            ? Math.Abs(wall.CTE_perK - jacketMaterial.Value.CTE_perK)
            : 0.0;
        bool perStation = gasSideWallProfile_mm is { Length: > 0 };

        // Sprint G': isentropic-flow exponent for per-station static P.
        // γ → ∞-limit safety-clamped to 1.05 to keep the formula well-defined
        // for caller-supplied values close to 1 (which never occur for real
        // hot gas but the model shouldn't crash on bad data).
        double gamma = System.Math.Max(gasGamma, 0);
        double gammaExp = gamma > 0 ? gamma / System.Math.Max(gamma - 1.0, 0.05) : 0.0;
        double gammaHalf = gamma > 0 ? (gamma - 1.0) * 0.5 : 0.0;

        // Sprint C (#350): combined axial-bending — activated only when a
        // gimbal arm is configured. F_axial = Pc × A_throat, used as F_thrust
        // (conservative: real thrust = Cf × F_axial, Cf > 1 so bending moment
        // is slightly under-estimated vs. real TVC, but Cf ≈ 1.6 is small enough
        // that the under-estimate is conservative for the gate design intent).
        double f_axial_N = 0.0;
        double bendingMoment_Nm = 0.0;
        if (gimbalOffset_m > 0 && solver.Stations.Length > 0)
        {
            int throatIdx = FindThroatStationIndex(solver);
            double rThroat_m = solver.Stations[throatIdx].R_mm * 1e-3;
            f_axial_N = chamberPressure_Pa * Math.PI * rThroat_m * rThroat_m;
            bendingMoment_Nm = f_axial_N * gimbalOffset_m;
        }
        double peakAxialBendingVM_MPa = 0.0;
        double peakAxialBendingYield_MPa = 0.0;

        for (int i = 0; i < solver.Stations.Length; i++)
        {
            var s = solver.Stations[i];
            double r_m = s.R_mm * 1e-3;

            // Track B: select per-station gas-side wall thickness when a
            // profile is provided, else fall back to the scalar (legacy /
            // uniform-thickness designs).
            double t_inner_mm = perStation && i < gasSideWallProfile_mm!.Length
                ? gasSideWallProfile_mm[i]
                : gasSideWallThickness_mm;
            double t_inner_m = t_inner_mm * 1e-3;
            double t_eff_m = t_inner_m + t_jacket_m;

            // Sprint G' (2026-04-27): per-station gas static pressure, replacing
            // the pre-G' constant-Pc gas-side floor that inflated exit-station
            // hoop ~10-20× because real exit gas P is ≈ 0.1 MPa, not Pc.
            // Isentropic-flow:  P_s = P_c · (1 + (γ-1)/2 · M²)^(-γ/(γ-1))
            // Convergent-section + chamber stations (M ≈ 0.1-0.3) → P_s ≈ Pc.
            // Throat (M = 1)  → P_s ≈ 0.55 · Pc (typical γ).
            // Exit (M ≈ 4 for ε = 84) → P_s ≈ 0.001 · Pc.
            //
            // gasGamma = 0 is the back-compat path: keep using chamberPressure_Pa
            // as the gas-side reference for legacy callers (test fixtures).
            double pGas_Pa;
            if (gamma > 0 && s.Mach > 0)
            {
                double machBracket = 1.0 + gammaHalf * s.Mach * s.Mach;
                pGas_Pa = chamberPressure_Pa * System.Math.Pow(machBracket, -gammaExp);
            }
            else
            {
                pGas_Pa = chamberPressure_Pa;
            }

            // Pressure differential across the inner wall: coolant side is at
            // P_coolant, gas side at LOCAL gas static P (post-G').
            //
            // **Sprint feasibility-audit-7 (2026-04-26 evening):** pre-fix
            // used `Math.Max(P_coolant, P_chamber)` for "startup/shutdown
            // conservatism" — but that overstates steady-state hoop stress by
            // ~2× (treats each side's pressure as if the other were vacuum)
            // and was the dominant contributor to YIELD_EXCEEDED firing on
            // 100 % of all canonical-preset SA candidates. Real engines pass
            // proof testing AT pressure (both sides loaded simultaneously);
            // startup/shutdown structural margin is a separate transient
            // analysis, not the steady-state gate target.
            //
            // **Sprint G' (2026-04-27):** the post-audit-7 formula
            // `max(|P_coolant − P_chamber|, P_chamber)` was retained as a
            // safety floor when gasGamma = 0 (legacy callers). When
            // gasGamma > 0, the floor is dropped because the per-station
            // gas static P is the correct steady-state envelope; the prior
            // floor was a stand-in for "gas P at this station" that
            // out-stated the truth at all post-throat stations.
            double pCoolantFloored = System.Math.Max(s.CoolantBulkPressure_Pa, 0);
            double absNetDP_Pa = System.Math.Abs(pCoolantFloored - pGas_Pa);
            double dP_Pa = gamma > 0
                ? absNetDP_Pa
                : System.Math.Max(absNetDP_Pa, chamberPressure_Pa);
            double sigmaHoop_Pa = dP_Pa * r_m / Math.Max(t_eff_m, 1e-6);

            // Thermal stress
            double Twg = s.GasSideWallTemp_K;
            double Twc = s.CoolantSideWallTemp_K;
            double dT = Twg - Twc;
            double T_mean = 0.5 * (Twg + Twc);
            double alpha = wall.CTE_perK;
            double E_Pa = wall.ElasticModulusAt_GPa(T_mean) * 1e9;
            double sigmaThermal_Pa = alpha * E_Pa * dT / (2.0 * (1.0 - DefaultPoisson));

            // Axial stress in a regen shell is complicated; for first-order
            // estimate, use σ_axial ≈ σ_hoop / 2 (closed thin-cylinder).
            double sigmaAxial_Pa = 0.5 * sigmaHoop_Pa;

            // von Mises with three principal stresses approx (σ1=hoop, σ2=axial, σ3 through-thickness ≈ 0)
            // Thermal stress is assumed to add in the hoop direction (fire-side skin tension).
            double s1 = sigmaHoop_Pa + sigmaThermal_Pa;
            double s2 = sigmaAxial_Pa;
            double s3 = 0;
            double vm = Math.Sqrt(0.5 * ((s1 - s2) * (s1 - s2)
                                       + (s2 - s3) * (s2 - s3)
                                       + (s3 - s1) * (s3 - s1)));

            // Z2.6 + A1-follow-on combined (2026-04-28):
            //   - Inner liner: σ_y at the GAS-SIDE wall temperature Twg
            //     (Z2.6: hoop + thermal-tension peak on the gas-side surface,
            //     so the hottest face's yield governs; strictly conservative
            //     vs the legacy through-wall-mean choice, and especially
            //     non-conservative-flipping for liner-dominated bimetallic
            //     where GRCop-42 sees a much hotter Twg than IN625 sees at
            //     coolant bulk T). External-audit Bug #2.
            //   - Outer jacket (when supplied): σ_y at the local coolant
            //     bulk T — the jacket is wetted by or adjacent to the
            //     cooling channel. Composite yield is the thickness-weighted
            //     (= hoop-load-weighted) blend, conservative pure bound
            //     with no stiffness credit. A1-follow-on bi-layer accounting.
            double sigmaY_inner = wall.YieldStrengthAt_MPa(Twg);
            double sigmaY_MPa;
            if (jacketMaterial.HasValue && t_jacket_m > 0)
            {
                double T_jkt = System.Math.Max(s.CoolantBulkTemp_K, 200.0);
                double sigmaY_jkt = jacketMaterial.Value.YieldStrengthAt_MPa(T_jkt);
                double t_jkt_mm = t_jacket_m * 1e3;
                sigmaY_MPa = (sigmaY_inner * t_inner_mm + sigmaY_jkt * t_jkt_mm)
                           / (t_inner_mm + t_jkt_mm);
            }
            else
            {
                sigmaY_MPa = sigmaY_inner;
            }
            double sigmaY_Pa = sigmaY_MPa * 1e6;
            double SF = sigmaY_Pa / Math.Max(vm, 1);

            if (sigmaHoop_Pa / 1e6 > peakHoop) peakHoop = sigmaHoop_Pa / 1e6;
            if (sigmaThermal_Pa / 1e6 > peakThermal) peakThermal = sigmaThermal_Pa / 1e6;
            if (vm / 1e6 > peakCombined) { peakCombined = vm / 1e6; peakIdx = i; }
            if (SF < minSF) minSF = SF;
            if (sigmaY_MPa < minSigmaY_MPa) minSigmaY_MPa = sigmaY_MPa;
            if (SF < 1.0) yielded = true;

            // Z3-M2: per-station bond-zone shear τ = ΔT·|Δα|·E_eff.
            // E_eff is the arithmetic mean of liner + jacket moduli at the
            // wall mean temperature — a first-order conservative estimate.
            // Only computed when jacketMaterial is present and CTEs differ.
            if (deltaAlpha > 1e-12)
            {
                double E_eff_GPa = 0.5 * (wall.ElasticModulusAt_GPa(T_mean)
                                        + jacketMaterial!.Value.ElasticModulusAt_GPa(T_mean));
                double tau_MPa = deltaAlpha * Math.Abs(dT) * E_eff_GPa * 1000.0;
                if (tau_MPa > peakBondShear_MPa) peakBondShear_MPa = tau_MPa;
            }

            // Sprint C (#350): combined axial-bending VM (Hibbeler §8.4).
            // σ_axial_membrane = F_axial / (π·D_mean·t_eff)  — thin-wall annular area
            // σ_bending        = M·c / I                      — extreme tensile fiber
            // σ_VM             = √(σ_h² + σ_a² − σ_h·σ_a)   — biaxial, no shear
            double axialBendingVM_MPa = 0.0;
            if (gimbalOffset_m > 0)
            {
                double d_i_m    = 2.0 * r_m;
                double d_o_m    = d_i_m + 2.0 * t_eff_m;
                double d_mean_m = 0.5 * (d_i_m + d_o_m);
                double sigmaAxialMem_Pa = f_axial_N
                    / Math.Max(Math.PI * d_mean_m * t_eff_m, 1e-20);
                double i_m4 = Math.PI / 64.0
                    * (d_o_m * d_o_m * d_o_m * d_o_m - d_i_m * d_i_m * d_i_m * d_i_m);
                double sigmaBending_Pa = bendingMoment_Nm * (d_o_m / 2.0)
                    / Math.Max(i_m4, 1e-30);
                double sigmaAxialTotal_Pa = sigmaAxialMem_Pa + sigmaBending_Pa;
                double vmAB_Pa = Math.Sqrt(
                    sigmaHoop_Pa * sigmaHoop_Pa
                    + sigmaAxialTotal_Pa * sigmaAxialTotal_Pa
                    - sigmaHoop_Pa * sigmaAxialTotal_Pa);
                axialBendingVM_MPa = vmAB_Pa / 1e6;
                if (axialBendingVM_MPa > peakAxialBendingVM_MPa)
                {
                    peakAxialBendingVM_MPa  = axialBendingVM_MPa;
                    peakAxialBendingYield_MPa = sigmaY_MPa;
                }
            }

            stations[i] = new StationStressResult(
                Index: i,
                X_mm: s.X_mm,
                HoopStress_MPa: sigmaHoop_Pa / 1e6,
                ThermalStress_MPa: sigmaThermal_Pa / 1e6,
                CombinedVonMises_MPa: vm / 1e6,
                YieldAtTemp_MPa: sigmaY_MPa,
                SafetyFactor: SF,
                AxialBendingVM_MPa: axialBendingVM_MPa);
        }

        double bondShearRatio = 0.0;
        if (peakBondShear_MPa > 0 && minSigmaY_MPa < double.MaxValue && minSigmaY_MPa > 0)
            bondShearRatio = peakBondShear_MPa / (minSigmaY_MPa * 0.5);

        return new StructuralSummary(
            Stations: stations,
            PeakHoop_MPa: peakHoop,
            PeakThermal_MPa: peakThermal,
            PeakCombined_MPa: peakCombined,
            MinSafetyFactor: minSF == double.MaxValue ? 0 : minSF,
            PeakStationIndex: peakIdx,
            YieldExceeded: yielded,
            BondZoneShearStress_MPa: peakBondShear_MPa,
            BondZoneShearRatio: bondShearRatio,
            PeakAxialBendingVM_MPa: peakAxialBendingVM_MPa,
            PeakAxialBendingYield_MPa: peakAxialBendingYield_MPa);
    }
}
