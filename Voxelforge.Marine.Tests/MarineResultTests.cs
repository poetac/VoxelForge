// MarineResultTests.cs — direct ctor + property tests for the MarineResult
// record. Per audit 05-test-gaps.md §4 the type was previously exercised
// only transitively through MarineOptimization.GenerateWith and never
// named directly in a test file.

using System.Collections.Generic;
using Voxelforge.Marine;
using Voxelforge.Optimization;
using Xunit;
using static Voxelforge.Marine.Tests.ScaffoldingSmokeTests;

namespace Voxelforge.Marine.Tests;

public sealed class MarineResultTests
{
    // ── Ctor / record fields ─────────────────────────────────────────────

    private static MarineResult MakeMinimalFeasible()
    {
        var design = MakeRemus100Design();
        var cond   = MakeRemus100Conditions();
        return new MarineResult(
            Design:                       design,
            Conditions:                   cond,
            DragForce_N:                  5.5,
            DragCoefficient:              0.30,
            BuoyancyForce_N:              350.0,
            DisplacedVolume_m3:           0.035,
            BuoyantWeight_N:              25.0,
            CriticalBucklingPressure_Pa:  4.5e6,
            BucklingSafetyFactor:         3.0,
            HullMass_kg:                  32.0,
            CgCbOffset_m:                 0.0,
            Violations:                   new List<FeasibilityViolation>(),
            Advisories:                   new List<FeasibilityViolation>(),
            IsFeasible:                   true);
    }

    [Fact]
    public void Ctor_StoresAllPositionalFields()
    {
        var r = MakeMinimalFeasible();
        Assert.Equal(MarineKind.AuvMidBody,             r.Design.Kind);
        Assert.Equal(5.5,                               r.DragForce_N);
        Assert.Equal(0.30,                              r.DragCoefficient);
        Assert.Equal(350.0,                             r.BuoyancyForce_N);
        Assert.Equal(0.035,                             r.DisplacedVolume_m3);
        Assert.Equal(25.0,                              r.BuoyantWeight_N);
        Assert.Equal(4.5e6,                             r.CriticalBucklingPressure_Pa);
        Assert.Equal(3.0,                               r.BucklingSafetyFactor);
        Assert.Equal(32.0,                              r.HullMass_kg);
        Assert.Equal(0.0,                               r.CgCbOffset_m);
        Assert.Empty(r.Violations);
        Assert.Empty(r.Advisories);
        Assert.True(r.IsFeasible);
    }

    [Fact]
    public void InitOnly_PlaningFields_DefaultToNaN()
    {
        // M.W3 SurfaceHull (Planing) fields are NaN-by-default; an AUV
        // result that round-trips through the ctor leaves them at NaN.
        var r = MakeMinimalFeasible();
        Assert.True(double.IsNaN(r.TrimAngle_deg));
        Assert.True(double.IsNaN(r.WettedLengthToBeamRatio));
        Assert.True(double.IsNaN(r.SpeedCoefficient));
        Assert.True(double.IsNaN(r.WettedSurfaceArea_m2));
    }

    [Fact]
    public void WithExpression_OverridesInitOnly_PlaningFields()
    {
        // The record's `with` expression must propagate init-only Planing
        // overrides (Sprint M.W3 surface hulls populate these post-ctor).
        var r = MakeMinimalFeasible() with
        {
            TrimAngle_deg            = 5.5,
            WettedLengthToBeamRatio  = 3.8,
            SpeedCoefficient         = 1.9,
            WettedSurfaceArea_m2     = 22.4,
        };
        Assert.Equal(5.5,  r.TrimAngle_deg);
        Assert.Equal(3.8,  r.WettedLengthToBeamRatio);
        Assert.Equal(1.9,  r.SpeedCoefficient);
        Assert.Equal(22.4, r.WettedSurfaceArea_m2);
    }

    // ── IsFeasible echo-back ─────────────────────────────────────────────

    [Fact]
    public void IsFeasible_CarriesThroughCtor_True()
    {
        var r = MakeMinimalFeasible();
        Assert.True(r.IsFeasible);
    }

    [Fact]
    public void IsFeasible_CarriesThroughCtor_False()
    {
        var design = MakeRemus100Design();
        var cond   = MakeRemus100Conditions();
        var violations = new List<FeasibilityViolation>
        {
            new(ConstraintId: "BUCKLING_MARGIN_INSUFFICIENT",
                Description:  "Buckling SF 1.2 < 1.5",
                ActualValue:  1.2,
                Limit:        1.5),
        };
        var r = new MarineResult(
            design, cond, 5.0, 0.30, 200.0, 0.030, -10.0, 1.5e6, 1.2, 35.0, 0.0,
            violations, new List<FeasibilityViolation>(), IsFeasible: false);
        Assert.False(r.IsFeasible);
        Assert.Single(r.Violations);
        Assert.Equal("BUCKLING_MARGIN_INSUFFICIENT", r.Violations[0].ConstraintId);
    }

    // ── IEngineResult marker ─────────────────────────────────────────────

    [Fact]
    public void MarineResult_ImplementsIEngineResult()
    {
        var r = MakeMinimalFeasible();
        Assert.IsAssignableFrom<Voxelforge.Engines.IEngineResult>(r);
    }

    // ── Pareto-projection sanity (audit §4) ──────────────────────────────

    [Fact]
    public void EndToEnd_FromOptimization_PopulatesScalarFields()
    {
        // Smoke check: a fully end-to-end MarineOptimization result must
        // populate every primary scalar with finite values for a feasible
        // REMUS-100 baseline. Catches regressions where a solver leaves a
        // field at NaN (silent infeasibility downstream).
        var r = MarineOptimization.GenerateWith(MakeRemus100Design(),
            MakeRemus100Conditions());
        Assert.False(double.IsNaN(r.DragForce_N));
        Assert.False(double.IsNaN(r.BuoyancyForce_N));
        Assert.False(double.IsNaN(r.DisplacedVolume_m3));
        Assert.False(double.IsNaN(r.HullMass_kg));
        Assert.False(double.IsNaN(r.CriticalBucklingPressure_Pa));
        Assert.False(double.IsNaN(r.BucklingSafetyFactor));
    }
}
