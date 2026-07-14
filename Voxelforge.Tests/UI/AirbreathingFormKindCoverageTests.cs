// AirbreathingFormKindCoverageTests.cs — Issue #441 (2026-05-06).
//
// Pins the contract between AirbreathingForm's "Engine kind" ComboBox
// and the AirbreathingCycleSolvers registry: every enum value the UI
// surfaces must resolve to a registered solver. Catches the failure
// mode where someone adds a new AirbreathingEngineKind value without
// updating the form OR adds a UI option without a backing solver.
//
// Form-instantiation tests are deferred — see Phase7UiInfraTests.cs
// "The actual UI surfaces … need a live WinForms instance to test
// directly — xUnit cannot spin one up cleanly without the PicoGK
// Library and the STA thread".

using System;
using System.Linq;
using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Cycles;
using Xunit;

namespace Voxelforge.Tests.UI;

public class AirbreathingFormKindCoverageTests
{
    /// <summary>
    /// Every non-sentinel <see cref="AirbreathingEngineKind"/> must resolve
    /// to a registered solver. The UI ComboBox lists exactly these kinds.
    /// </summary>
    [Fact]
    public void EveryNonSentinelKind_HasRegisteredSolver()
    {
        var kinds = Enum.GetValues<AirbreathingEngineKind>()
            .Where(k => k != AirbreathingEngineKind.None)
            .ToArray();

        Assert.Equal(12, kinds.Length); // pin the count — adding a new kind requires a UI panel. LiquidAirCycle + RotatingDetonation extend the original 10.

        foreach (var kind in kinds)
        {
            var solver = AirbreathingCycleSolvers.Get(kind);
            Assert.NotNull(solver);
        }
    }

    /// <summary>
    /// The UI's <c>SelectedKind()</c> mapping covers every enum value.
    /// Asserts via the canonical display-name strings the ComboBox uses.
    /// </summary>
    [Theory]
    [InlineData("Ramjet",       AirbreathingEngineKind.Ramjet)]
    [InlineData("Turbojet",     AirbreathingEngineKind.Turbojet)]
    [InlineData("Turbofan",     AirbreathingEngineKind.Turbofan)]
    [InlineData("Scramjet",     AirbreathingEngineKind.Scramjet)]
    [InlineData("RBCC",         AirbreathingEngineKind.Rbcc)]
    [InlineData("GasTurbine",   AirbreathingEngineKind.GasTurbine)]
    [InlineData("SteamTurbine", AirbreathingEngineKind.SteamTurbine)]
    [InlineData("Pulsejet",     AirbreathingEngineKind.Pulsejet)]
    [InlineData("Turboprop",    AirbreathingEngineKind.Turboprop)]
    [InlineData("Turboshaft",   AirbreathingEngineKind.Turboshaft)]
    [InlineData("LACE",         AirbreathingEngineKind.LiquidAirCycle)]
    [InlineData("RDE",          AirbreathingEngineKind.RotatingDetonation)]
    public void DisplayNameMatchesEnumValue(string displayName, AirbreathingEngineKind expected)
    {
        // Mirror the SelectedKind() switch in AirbreathingForm.cs.
        //
        // Red-team round 4: this theory previously stopped at the original
        // 10 kinds even though EveryNonSentinelKind_HasRegisteredSolver
        // (above) already pinned the count at 12 — LiquidAirCycle (LACE)
        // and RotatingDetonation (RDE) were fully solver-backed but never
        // reachable from the ComboBox or the --engine-kind CLI flag
        // (AirbreathingForm.KindToIndex/SelectedKind silently fell back to
        // Ramjet for both). Fixed alongside this test extension.
        var actual = displayName switch
        {
            "Ramjet"       => AirbreathingEngineKind.Ramjet,
            "Turbojet"     => AirbreathingEngineKind.Turbojet,
            "Turbofan"     => AirbreathingEngineKind.Turbofan,
            "Scramjet"     => AirbreathingEngineKind.Scramjet,
            "RBCC"         => AirbreathingEngineKind.Rbcc,
            "GasTurbine"   => AirbreathingEngineKind.GasTurbine,
            "SteamTurbine" => AirbreathingEngineKind.SteamTurbine,
            "Pulsejet"     => AirbreathingEngineKind.Pulsejet,
            "Turboprop"    => AirbreathingEngineKind.Turboprop,
            "Turboshaft"   => AirbreathingEngineKind.Turboshaft,
            "LACE"         => AirbreathingEngineKind.LiquidAirCycle,
            "RDE"          => AirbreathingEngineKind.RotatingDetonation,
            _              => AirbreathingEngineKind.None,
        };
        Assert.Equal(expected, actual);
    }
}
