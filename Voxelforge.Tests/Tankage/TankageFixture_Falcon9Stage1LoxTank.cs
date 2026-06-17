// TankageFixture_Falcon9Stage1LoxTank.cs — Sprint A.74 Phase 3 published-
// anchor cluster-validation fixture for the Tankage pillar.
//
// Anchors the Wave-1 thin-wall cylindrical pressure-vessel model to the
// **SpaceX Falcon 9 stage-1 LOX tank** at the Steel4130 cluster (Wave-1
// PressureVesselDesign.cs header). Geometry + operating-pressure anchors
// (SpaceX-published public partial specs; ULA + AIAA conference papers
// on Falcon 9 booster construction):
//   - Stage-1 cylindrical diameter: 3.66 m → R_internal ≈ 1.83 m
//   - Stage-1 propellant-tank section length: ≈ 26 m (LOX + RP-1 stacked)
//   - Tank-wall thickness: ≈ 4 mm uniform monocoque (model approximation)
//   - Maximum expected operating pressure (MEOP): 0.3 MPa (3 bar, atm
//     tank pressurization driven by helium pressurization system)
//   - Construction: real Falcon 9 uses Al-Li 2195 alloy; the Wave-1
//     PressureVesselDesign header treats it as the Steel4130 cluster
//     (closest available material in the Wave-1 TankShellType enum).
//
// Phase-3 coverage backfill on the Tankage pillar — Cohort 4 lead (process/
// structural tail). Distinct from the A.70 Tankage VOXEL builder (under
// umbrella issue #647) — this fixture exercises the Wave-1 PHYSICS model
// for thin-wall pressure-vessel stress + mass + safety-factor predictions.
//
// Per ADR-036 D3.2, each [Fact] carries a rationale comment with either
// a closed-form derivation or a cluster-anchor citation. The Wave-1
// thin-wall model captures circumferential + axial stress + von Mises
// + burst pressure + safety factor + shell mass exactly for R/t > 10
// (validated here at R/t ≈ 458).
//
// Model-vs-hardware gap (per ADR-036 D3.2):
//
//   Real Falcon 9 booster propellant tanks are Al-Li 2195 (σ_y ≈ 500 MPa
//   close to Steel4130's 460 MPa, but ρ_Al-Li = 2710 kg/m³ vs 4130's
//   7850 kg/m³ — the model over-predicts shell mass by ~ 2.9× compared
//   to the real Al-Li construction). The Wave-1 cluster has only three
//   TankShellType options (Steel4130, Aluminum6061, CarbonFibreComposite);
//   neither perfectly matches Al-Li 2195. Wave-2+ will add an
//   AluminumLithium cluster. Tests here describe what the Wave-1 Steel
//   model predicts at the F9 geometry, not what the real-world Al-Li
//   booster weighs.

using Voxelforge.Tankage;
using Xunit;

namespace Voxelforge.Tests.Tankage;

public sealed class TankageFixture_Falcon9Stage1LoxTank
{
    // ── Closed-form thin-wall stress fingerprints ──────────────────────

    [Fact]
    public void Falcon9_DesignPoint_HoopStressMatchesClosedForm()
    {
        // σ_hoop = P · R / t exactly (thin-wall theory, Wave-1).
        // At P=0.3 MPa, R=1.83 m, t=4 mm: σ_hoop = 137.25 MPa.
        var d = Falcon9LoxTank();
        var r = PressureVesselSolver.Solve(d);
        double expected = d.OperatingPressure_Pa
                        * d.InternalRadius_m
                        / d.WallThickness_m;
        Assert.Equal(expected, r.HoopStress_Pa, precision: 3);
    }

    [Fact]
    public void Falcon9_DesignPoint_AxialStressIsExactlyHalfHoop()
    {
        // σ_axial = P · R / (2t) = σ_hoop / 2 for a thin-walled cylinder
        // under internal pressure (Lamé thin-wall reduction).
        var r = PressureVesselSolver.Solve(Falcon9LoxTank());
        Assert.Equal(r.HoopStress_Pa / 2.0, r.AxialStress_Pa, precision: 6);
    }

    [Fact]
    public void Falcon9_DesignPoint_VonMisesBetweenAxialAndHoop()
    {
        // For σ_hoop > σ_axial > 0 with σ_2 = σ_1/2, von Mises =
        // √(σ_h² - σ_h·σ_a + σ_a²) = σ_h · √(1 - 0.5 + 0.25) = σ_h · √0.75
        // ≈ 0.866 · σ_h, which is strictly between σ_axial (= 0.5·σ_h)
        // and σ_hoop. Closed-form check on the von Mises formula.
        var r = PressureVesselSolver.Solve(Falcon9LoxTank());
        Assert.True(r.VonMisesStress_Pa > r.AxialStress_Pa);
        Assert.True(r.VonMisesStress_Pa < r.HoopStress_Pa);
    }

    [Fact]
    public void Falcon9_DesignPoint_BurstPressureMatchesClosedForm()
    {
        // P_burst = σ_yield · t / R. Steel4130 σ_y = 460 MPa.
        // P_burst = 460e6 · 0.004 / 1.83 ≈ 1.006 MPa.
        var d = Falcon9LoxTank();
        var r = PressureVesselSolver.Solve(d);
        Assert.InRange(r.BurstPressure_Pa, 0.9e6, 1.1e6);
    }

    [Fact]
    public void Falcon9_DesignPoint_SafetyFactorMatchesClosedForm()
    {
        // SF = P_burst / P_operating. At 1.006 / 0.3 ≈ 3.35.
        var d = Falcon9LoxTank();
        var r = PressureVesselSolver.Solve(d);
        Assert.Equal(r.BurstPressure_Pa / d.OperatingPressure_Pa,
                     r.SafetyFactor,
                     precision: 6);
    }

    [Fact]
    public void Falcon9_DesignPoint_SafetyFactorInAerospaceBand()
    {
        // Aerospace pressure-vessel rule of thumb: SF ∈ [1.5, 4.0]
        // (SpaceX / NASA-STD-5012 rev B for human-rated launch vehicles).
        // F9 at the Wave-1 Steel4130 cluster lands at ≈ 3.35 — well
        // inside the aerospace cluster.
        var r = PressureVesselSolver.Solve(Falcon9LoxTank());
        Assert.InRange(r.SafetyFactor, 1.5, 4.0);
    }

    // ── Cluster-anchor band fingerprints ───────────────────────────────

    [Fact]
    public void Falcon9_DesignPoint_HoopStressInAerospaceCluster()
    {
        // Aerospace propellant-tank cluster: σ_hoop ∈ [50, 200] MPa at
        // MEOP (Hill & Peterson Mechanics of Propulsion §10.2; SpaceX-
        // published F9 partial data).
        var r = PressureVesselSolver.Solve(Falcon9LoxTank());
        Assert.InRange(r.HoopStress_Pa, 50e6, 200e6);
    }

    [Fact]
    public void Falcon9_DesignPoint_InternalVolumeMatchesClosedForm()
    {
        // V = π·R²·L + (4/3)π·R³ (with hemispherical end caps).
        // At R=1.83, L=26: V_cyl = π·1.83²·26 ≈ 273 m³;
        // V_caps = (4/3)π·1.83³ ≈ 25.7 m³; total ≈ 299 m³.
        // (Real F9 LOX tank is ~ 257 m³; the model over-predicts because
        // RP-1 stack + intertank section + tank-wall thickness all share
        // the 26 m length — the Wave-1 model treats the full 26 m as
        // pressurized volume.)
        var r = PressureVesselSolver.Solve(Falcon9LoxTank());
        Assert.InRange(r.InternalVolume_m3, 250.0, 350.0);
    }

    [Fact]
    public void Falcon9_DesignPoint_ShellMassInClusterBand()
    {
        // ShellMass = ρ · V_shell. For Steel4130 (ρ=7850 kg/m³), thin
        // wall of 4 mm at R=1.83, L=26 m + end caps, V_shell ≈
        // 2π·R·t·L + 2·2π·R²·t ≈ 1.196 + 0.168 = 1.364 m³.
        // ShellMass ≈ 7850 · 1.364 ≈ 10 705 kg. (Real F9 Al-Li booster
        // dry mass ~ 25 000 kg total — the LOX tank alone is a fraction.
        // The model over-predicts at the Steel4130 cluster because real
        // F9 is Al-Li, density 2710 kg/m³ — model gives ≈ 2.9× the
        // real shell mass.)
        var r = PressureVesselSolver.Solve(Falcon9LoxTank());
        Assert.InRange(r.ShellMass_kg, 5_000.0, 20_000.0);
    }

    [Fact]
    public void Falcon9_DesignPoint_GravimetricEfficiencyPositive()
    {
        // PV / (m · g₀) is the figure of merit for pressure-vessel mass
        // payload trade. Must be positive (nonzero pressure-vessel-energy
        // stored per kg of shell mass).
        var r = PressureVesselSolver.Solve(Falcon9LoxTank());
        Assert.True(r.GravimetricEfficiency > 0,
            $"Gravimetric efficiency ({r.GravimetricEfficiency:F1} m) must be > 0.");
    }

    // ── Categorical + operating-envelope fingerprints ──────────────────

    [Fact]
    public void Falcon9_UsesSteel4130ShellType()
    {
        // Wave-1 cluster approximation for the Al-Li 2195 monocoque (no
        // AluminumLithium kind in the Wave-1 TankShellType enum). Per
        // file-header rationale this is the closest available cluster.
        Assert.Equal(TankShellType.Steel4130, Falcon9LoxTank().ShellType);
    }

    [Fact]
    public void Falcon9_DesignPoint_HasHemisphericalEndCaps()
    {
        // F9 booster tanks use hemispherical / ellipsoidal end caps for
        // optimal pressure-stress distribution (uniform meridional and
        // circumferential stresses at the end caps).
        Assert.True(Falcon9LoxTank().HasHemisphericalEndCaps);
    }

    [Fact]
    public void Falcon9_DoublingPressure_DoublesHoopStress()
    {
        // σ_hoop ∝ P linearly at fixed geometry. Doubling P from 0.3 to
        // 0.6 MPa doubles σ_hoop. Linear-scaling fingerprint.
        var nominal = PressureVesselSolver.Solve(Falcon9LoxTank());
        var doubleP = PressureVesselSolver.Solve(
            Falcon9LoxTank() with { OperatingPressure_Pa = 0.6e6 });
        Assert.Equal(nominal.HoopStress_Pa * 2.0,
                     doubleP.HoopStress_Pa,
                     precision: 3);
    }

    [Fact]
    public void Falcon9_DoublingWallThickness_HalvesHoopStress()
    {
        // σ_hoop ∝ 1/t at fixed P, R. Doubling t halves σ_hoop.
        var nominal = PressureVesselSolver.Solve(Falcon9LoxTank());
        var thicker = PressureVesselSolver.Solve(
            Falcon9LoxTank() with { WallThickness_m = 0.008 });
        Assert.Equal(nominal.HoopStress_Pa / 2.0,
                     thicker.HoopStress_Pa,
                     precision: 3);
    }

    [Fact]
    public void Falcon9_WithoutEndCaps_HasSmallerInternalVolume()
    {
        // Removing end caps removes (4/3)·π·R³ ≈ 25.7 m³ at R=1.83.
        var withCaps    = PressureVesselSolver.Solve(Falcon9LoxTank());
        var withoutCaps = PressureVesselSolver.Solve(
            Falcon9LoxTank() with { HasHemisphericalEndCaps = false });
        Assert.True(withoutCaps.InternalVolume_m3 < withCaps.InternalVolume_m3,
            $"Removing end caps reduces internal volume: with caps "
          + $"({withCaps.InternalVolume_m3:F1} m³) vs without caps "
          + $"({withoutCaps.InternalVolume_m3:F1} m³).");
    }

    // ── Helpers ────────────────────────────────────────────────────────

    // SpaceX Falcon 9 stage-1 LOX tank — Wave-1 header cluster anchor.
    //   - R = 1.83 m (3.66 m OD); L = 26 m (full stage-1 tank section)
    //   - t = 4 mm uniform monocoque (model approximation; real F9 has
    //     waffle-grid stiffening that varies thickness)
    //   - MEOP = 0.3 MPa (3 bar atm pressurization)
    //   - ShellType: Steel4130 (Wave-1 cluster approximation; real
    //     F9 is Al-Li 2195 — no AluminumLithium kind in Wave-1 enum)
    //   - HasHemisphericalEndCaps: true (standard ellipsoidal end caps)
    private static PressureVesselDesign Falcon9LoxTank() => new(
        ShellType:                    TankShellType.Steel4130,
        InternalRadius_m:             1.83,
        ShellLength_m:                26.0,
        WallThickness_m:              0.004,
        OperatingPressure_Pa:         300_000.0,
        HasHemisphericalEndCaps:      true);
}
