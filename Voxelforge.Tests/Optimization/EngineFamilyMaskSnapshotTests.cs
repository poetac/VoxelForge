// EngineFamilyMaskSnapshotTests.cs — Sprint 0 / Wave 1 (2026-05-05).
//
// Pins the bit assignments of `EngineFamilyMask` (defined in
// Voxelforge.Core/Optimization/GateRegistry.cs). Drift in the enum bit
// values would silently shift gate-applicability dispatch — every gate
// in the registry carries a `Applicability` mask, and the optimizer
// filters by `(gate.Applicability & familyMask) != 0`. Reordering or
// reassigning bits without intent breaks that filter.
//
// Mirrors the GateOrderingSnapshotTests pattern: concise snapshot
// strings; updates to the enum (e.g. uncommenting `Airbreathing = 1 << 2`
// when air-breathing gates land) must accompany an intentional snapshot
// update in the same PR.

using System;
using System.Linq;
using Voxelforge.Optimization;

// File lives in Optimization/ for organization but uses the flat
// `Voxelforge.Tests` namespace — see GateOrderingSnapshotTests.cs comment
// for the rationale (a nested `Voxelforge.Tests.Optimization` namespace
// would shadow `Optimization.X` references in sibling test files).
namespace Voxelforge.Tests;

public sealed class EngineFamilyMaskSnapshotTests
{
    [Fact]
    public void EngineFamilyMask_BitAssignments_ArePinned()
    {
        // Walk every defined value in declaration order so we catch
        // additions, removals, and reorderings. The snapshot reads
        // each enum member as `Name = 0xHHHHHHHH` — easy to diff in
        // the failure message.
        var actual = string.Join(
            "\n",
            Enum.GetValues<EngineFamilyMask>()
                .Select(v => $"{v} = 0x{(int)v:X8}"));

        var expected = string.Join(
            "\n",
            "None = 0x00000000",
            "RocketRegen = 0x00000001",
            "RocketAerospike = 0x00000002",
            "Rocket = 0x00000003",
            "Airbreathing = 0x00000004",
            "ElectricPropulsion = 0x00000008",
            "NuclearPropulsion = 0x00000010",
            "ElectricResistojet = 0x00000080",
            "ElectricHallEffect = 0x00000100",
            "ElectricGriddedIon = 0x00000200",
            "ElectricArcjet = 0x00000400",
            "ElectricMpd = 0x00000800",
            "ElectricPpt = 0x00001000",
            "Marine = 0x00002000",
            "MarineHull = 0x00004000",
            "ElectricVasimr = 0x00008000",
            "ElectricFeep = 0x00010000",
            "ElectricHdlt = 0x00020000",
            "All = 0xFFFFFFFF");

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void EngineFamilyMask_RocketIsUnionOfRegenAndAerospike()
    {
        // The Rocket convenience value must equal RocketRegen | RocketAerospike.
        // If Airbreathing (1 << 2) gets uncommented, this stays valid; the
        // Rocket alias is rocket-only by design.
        Assert.Equal(
            EngineFamilyMask.RocketRegen | EngineFamilyMask.RocketAerospike,
            EngineFamilyMask.Rocket);
    }

    [Fact]
    public void EngineFamilyMask_AllIsBitwiseComplementOfZero()
    {
        // `All = ~0` per declaration. Pinning this prevents an accidental
        // narrowing that would silently exclude future families from
        // family-mask filters.
        Assert.Equal(unchecked((EngineFamilyMask)~0), EngineFamilyMask.All);
    }
}
