// VasimrReservedSlotTests.cs — VASIMR schema + family-mask invariants.
//
// Originally this file pinned the deferred-slot contract (Sprint EP.W4
// dispatch sentinel: enum value, family-mask bit, NotImplementedException
// dispatch with EP.W4-pointing message). As of Sprint A.64 (2026-05-18)
// the VASIMR helicon + ICRH + magnetic-nozzle solver shipped (Wave-3) and
// `GenerateWith(VasimrDesign, ...)` returns a real result — so the
// `Vasimr_Dispatch_ThrowsNotImplementedException` sentinel was removed in
// Sprint A.79 (this file). The remaining invariants are still load-bearing:
//
//   • Kind=Vasimr is a recognised enum value at the pinned ordinal slot
//     (schema-stability invariant; future schema migrations must not
//     reorder it).
//   • EngineFamilyMask.ElectricVasimr is registered at the reserved bit
//     so gate dispatch routes VASIMR results to VASIMR-specific gates
//     and not into another family's fallback bucket.
//
// Real VASIMR dispatch coverage lives in the published-engine fixture
// `VasimrVx200iFixture` + the Wave-3 solver tests under this project.

namespace Voxelforge.ElectricPropulsion.Tests;

public sealed class VasimrReservedSlotTests
{
    [Fact]
    public void Vasimr_EnumValue_IsSeven()
    {
        // Schema-stability invariant: the Vasimr slot must stay at value 7
        // (after PulsedPlasmaThruster = 6) so future schema migrations
        // don't reorder it.
        Assert.Equal(7, (int)ElectricPropulsionEngineKind.Vasimr);
    }

    [Fact]
    public void Vasimr_FamilyMaskBit_IsRegistered()
    {
        // The EngineFamilyMask bit reservation must be live so future
        // VASIMR-specific gates can register against it via the
        // GateRegistry. Without this bit, gate dispatch for Vasimr
        // results would silently fall through to other variants.
        Assert.Equal(1 << 15, (int)Voxelforge.Optimization.EngineFamilyMask.ElectricVasimr);
    }
}
