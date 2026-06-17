// ThrustTakeoutAdapterTests — pure-data + persistence coverage for the
// Hot-fire readiness Item 6 (#260) test-stand adapter.
//
// Sprint B-2 (2026-04-30). Covers:
//   1. Defaults on RegenChamberDesign (off-by-default; legacy designs unchanged)
//   2. ResolveOuterDiameter sentinel semantics ("0 = match flange OD")
//   3. ThrustTakeoutAdapterSpec invariants
//   4. DesignPersistence v23 round-trip with the new fields populated
//   5. BuildSheet.BuildMarkdown emits the adapter section iff both flags are on
//
// Voxel-build verification lives in ThrustTakeoutAdapterVoxelTests.cs
// (in-process under PicoGK 2.0.0; see CLAUDE.md pitfall #8 retired
// 2026-05-04 after PR #374).

using System.IO;
using Voxelforge.Combustion;
using Voxelforge.Geometry;
using Voxelforge.IO;
using Voxelforge.Optimization;
using Voxelforge.Tests.Helpers;
using Xunit;

namespace Voxelforge.Tests;

public class ThrustTakeoutAdapterTests
{
    [Fact]
    public void RegenChamberDesign_AdapterDefaultsOff()
    {
        var d = new RegenChamberDesign();
        // The whole point of the spec is that legacy designs see zero
        // behaviour change. Defaults must keep the adapter off.
        Assert.False(d.IncludeThrustTakeoutAdapter);
        Assert.Equal(50.0,                      d.ThrustTakeoutAdapterHeight_mm);
        Assert.Equal(0.0,                       d.ThrustTakeoutOuterDiameter_mm);
        Assert.Equal(MountingFlangeStandard.Generic8Bolt, d.ThrustTakeoutMountStandard);
        Assert.Equal(0,                         d.ThrustTakeoutUmbilicalPassThroughCount);
        Assert.Equal(8.0,                       d.ThrustTakeoutUmbilicalPassThroughDiameter_mm);
    }

    [Fact]
    public void ResolveOuterDiameter_ZeroFallsBackToFlangeOD()
    {
        // 0 sentinel → use the chamber mounting-flange OD.
        Assert.Equal(120.0, ThrustTakeoutAdapterGeometry.ResolveOuterDiameter(0.0,  120.0));
        Assert.Equal(120.0, ThrustTakeoutAdapterGeometry.ResolveOuterDiameter(-5.0, 120.0));
        // Non-zero positive → caller's value wins (allows flaring beyond
        // the chamber's OD for a wider stand-side mount face).
        Assert.Equal(180.0, ThrustTakeoutAdapterGeometry.ResolveOuterDiameter(180.0, 120.0));
    }

    [Fact]
    public void Spec_HoldsValuesWithoutMutation()
    {
        var spec = new ThrustTakeoutAdapterSpec(
            OuterDiameter_mm:                140.0,
            Height_mm:                       60.0,
            MountStandard:                   MountingFlangeStandard.MilStd_6Bolt_Clocked,
            UmbilicalPassThroughCount:       4,
            UmbilicalPassThroughDiameter_mm: 10.0);
        Assert.Equal(140.0, spec.OuterDiameter_mm);
        Assert.Equal( 60.0, spec.Height_mm);
        Assert.Equal(MountingFlangeStandard.MilStd_6Bolt_Clocked, spec.MountStandard);
        Assert.Equal(4,     spec.UmbilicalPassThroughCount);
        Assert.Equal(10.0,  spec.UmbilicalPassThroughDiameter_mm);
        Assert.Equal( 0.5,  spec.ExitClearanceRadius_mm);   // default
    }

    [Fact]
    public void DesignPersistence_RoundTripsAdapterFields()
    {
        var cond = new OperatingConditions
        {
            Thrust_N = 5_000,
            ChamberPressure_Pa = 6.9e6,
            MixtureRatio = 3.3,
            PropellantPair = PropellantPair.LOX_CH4,
        };
        var d = new RegenChamberDesign
        {
            IncludeMountingFlange                          = true,
            IncludeThrustTakeoutAdapter                    = true,
            ThrustTakeoutAdapterHeight_mm                  = 65.0,
            ThrustTakeoutOuterDiameter_mm                  = 150.0,
            ThrustTakeoutMountStandard                     = MountingFlangeStandard.MilStd_6Bolt_Clocked,
            ThrustTakeoutUmbilicalPassThroughCount         = 4,
            ThrustTakeoutUmbilicalPassThroughDiameter_mm   = 9.5,
        };

        using var tmp = TestTempFile.WithUniqueName("thrust-takeout-roundtrip", "json");
        DesignPersistence.Save(tmp.Path, cond, d, r: null);
        var loaded = DesignPersistence.Load(tmp.Path);

        Assert.NotNull(loaded);
        Assert.Equal(DesignPersistence.CurrentSchemaVersion, loaded!.Schema);
        var ld = loaded.Design!;
        Assert.True(ld.IncludeMountingFlange);
        Assert.True(ld.IncludeThrustTakeoutAdapter);
        Assert.Equal(65.0,   ld.ThrustTakeoutAdapterHeight_mm);
        Assert.Equal(150.0,  ld.ThrustTakeoutOuterDiameter_mm);
        Assert.Equal(MountingFlangeStandard.MilStd_6Bolt_Clocked, ld.ThrustTakeoutMountStandard);
        Assert.Equal(4,      ld.ThrustTakeoutUmbilicalPassThroughCount);
        Assert.Equal(9.5,    ld.ThrustTakeoutUmbilicalPassThroughDiameter_mm);
    }

    [Fact]
    public void DesignPersistence_PreV23File_LoadsWithAdapterDefaults()
    {
        // A v22 file (the prior schema) MUST load with adapter fields at
        // their C# init-only defaults. This is the back-compat contract:
        // legacy saved designs continue to round-trip identically through
        // the migration chain.
        const string v22Json = """
            {
              "Schema": "v22",
              "Version": "1.0",
              "Conditions": {
                "Thrust_N": 5000,
                "ChamberPressure_Pa": 6900000,
                "MixtureRatio": 3.3,
                "WallMaterialIndex": 1
              },
              "Design": {
                "ChamberRadius_mm": 30,
                "ThroatRadius_mm": 15
              }
            }
            """;

        using var tmp = TestTempFile.WithUniqueName("thrust-takeout-pre-v23", "json");
        File.WriteAllText(tmp.Path, v22Json);
        var loaded = DesignPersistence.Load(tmp.Path);

        Assert.NotNull(loaded);
        // OOB-6 / #200 (2026-04-30) bumped schema to v24; v22 input
        // climbs through both bumps. Pin to CurrentSchemaVersion so this
        // test stays correct after future bumps.
        Assert.Equal(IO.DesignPersistence.CurrentSchemaVersion, loaded!.Schema);
        var ld = loaded.Design!;
        Assert.False(ld.IncludeThrustTakeoutAdapter);
        Assert.Equal(50.0, ld.ThrustTakeoutAdapterHeight_mm);
        Assert.Equal(0.0,  ld.ThrustTakeoutOuterDiameter_mm);
    }

    [Fact]
    public void BuildSheet_NoAdapterSection_WhenAdapterFlagOff()
    {
        var cond = new OperatingConditions { Thrust_N = 5_000, ChamberPressure_Pa = 6.9e6, MixtureRatio = 3.3 };
        var d = new RegenChamberDesign
        {
            IncludeMountingFlange       = true,
            IncludeThrustTakeoutAdapter = false,
        };
        var md = BuildSheet.BuildMarkdown(cond, d);
        Assert.DoesNotContain("Test-stand thrust-takeout adapter", md);
    }

    [Fact]
    public void BuildSheet_NoAdapterSection_WhenMountingFlangeOff()
    {
        // Adapter requires the chamber's mounting flange to be on too —
        // there's nothing for the adapter to bolt to otherwise. BuildSheet
        // suppresses the section when the mounting flange is off, even if
        // someone set the adapter flag in isolation.
        var cond = new OperatingConditions { Thrust_N = 5_000, ChamberPressure_Pa = 6.9e6, MixtureRatio = 3.3 };
        var d = new RegenChamberDesign
        {
            IncludeMountingFlange       = false,
            IncludeThrustTakeoutAdapter = true,   // orphan flag — silently ignored
        };
        var md = BuildSheet.BuildMarkdown(cond, d);
        Assert.DoesNotContain("Test-stand thrust-takeout adapter", md);
    }

    [Fact]
    public void BuildSheet_EmitsAdapterSection_WhenBothFlagsOn()
    {
        var cond = new OperatingConditions
        {
            Thrust_N = 10_000, ChamberPressure_Pa = 10e6, MixtureRatio = 3.3,
            PropellantPair = PropellantPair.LOX_CH4,
        };
        var d = new RegenChamberDesign
        {
            IncludeMountingFlange                          = true,
            IncludeThrustTakeoutAdapter                    = true,
            ThrustTakeoutAdapterHeight_mm                  = 60.0,
            ThrustTakeoutOuterDiameter_mm                  = 0.0,   // sentinel
            ThrustTakeoutMountStandard                     = MountingFlangeStandard.MilStd_6Bolt_Clocked,
            ThrustTakeoutUmbilicalPassThroughCount         = 3,
            ThrustTakeoutUmbilicalPassThroughDiameter_mm   = 8.0,
        };
        var md = BuildSheet.BuildMarkdown(cond, d);

        Assert.Contains("## Test-stand thrust-takeout adapter", md);
        Assert.Contains("60.0 mm", md);                        // height
        Assert.Contains("match mounting-flange OD", md);       // OD sentinel rendered
        Assert.Contains("MIL-STD 6-bolt", md);                 // preset display name (substring)
        Assert.Contains("3× Ø8.0 mm (radial)", md);            // umbilical line
    }

    [Fact]
    public void BuildSheet_AdapterSection_RendersExplicitOD()
    {
        var cond = new OperatingConditions { Thrust_N = 10_000, ChamberPressure_Pa = 10e6, MixtureRatio = 3.3 };
        var d = new RegenChamberDesign
        {
            IncludeMountingFlange                  = true,
            IncludeThrustTakeoutAdapter            = true,
            ThrustTakeoutOuterDiameter_mm          = 175.0,
            ThrustTakeoutUmbilicalPassThroughCount = 0,
        };
        var md = BuildSheet.BuildMarkdown(cond, d);

        Assert.Contains("175.0 mm", md);
        Assert.Contains("| Umbilical pass-throughs | none |", md);
    }

    [Fact]
    public void Spec_RejectsZeroHeight()
    {
        // The voxel builder explicitly throws on degenerate (height ≤ 0)
        // specs — adapter with no height is a no-op caller error, not a
        // valid configuration. Pinned here so the contract isn't quietly
        // relaxed by a future refactor. Spec record allows the value
        // through (records don't validate by design); the validation
        // lives in AddAdapterFull and is exercised by the in-process
        // voxel test ThrustTakeoutAdapterVoxelTests.
        var spec = new ThrustTakeoutAdapterSpec(
            OuterDiameter_mm:                100.0,
            Height_mm:                       0.0,
            MountStandard:                   MountingFlangeStandard.Generic8Bolt,
            UmbilicalPassThroughCount:       0,
            UmbilicalPassThroughDiameter_mm: 8.0);
        Assert.Equal(0.0, spec.Height_mm);
    }
}
