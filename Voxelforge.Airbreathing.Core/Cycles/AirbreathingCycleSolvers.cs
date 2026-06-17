// AirbreathingCycleSolvers.cs — kind → solver dispatch.
//
// Parallel to CycleSolvers on the rocket side. Sprint A1 ships an
// empty registry; A4 (ramjet) and A7 (turbojet) populate it.

using System.Collections.Generic;

namespace Voxelforge.Airbreathing.Cycles;

/// <summary>
/// Dispatch from <see cref="AirbreathingEngineKind"/> to the
/// <see cref="IAirbreathingCycleSolver"/> implementation that handles
/// it. Static registry — solvers are stateless and globally singleton.
/// </summary>
public static class AirbreathingCycleSolvers
{
    private static readonly Dictionary<AirbreathingEngineKind, IAirbreathingCycleSolver> _byKind = BuildRegistry();

    /// <summary>
    /// Look up the solver for the given kind. Throws
    /// <see cref="System.NotSupportedException"/> when the kind has
    /// no registered solver yet — only unknown future enum entries
    /// take this path post-A11 (all five shipped kinds are registered).
    /// </summary>
    public static IAirbreathingCycleSolver Get(AirbreathingEngineKind kind)
    {
        if (_byKind.TryGetValue(kind, out var solver)) return solver;
        throw new System.NotSupportedException(
            $"No air-breathing cycle solver is registered for kind '{kind}'. "
          + "Solvers ship per sub-step: ramjet "
          + "(A4), turbojet (A7), turbofan (A8), scramjet (A10), "
          + "RBCC (capstone).");
    }

    /// <summary>
    /// True when a solver is registered for <paramref name="kind"/>.
    /// Use to gate UI / CLI options to the kinds the current build
    /// actually supports.
    /// </summary>
    public static bool IsSupported(AirbreathingEngineKind kind)
        => _byKind.ContainsKey(kind);

    /// <summary>
    /// All registered kinds. Iteration order is registration order.
    /// </summary>
    public static IReadOnlyCollection<AirbreathingEngineKind> SupportedKinds => _byKind.Keys;

    private static Dictionary<AirbreathingEngineKind, IAirbreathingCycleSolver> BuildRegistry()
    {
        // Sprint A4 added RamjetCycleSolver; A7 added TurbojetCycleSolver;
        // A8 adds TurbofanCycleSolver + GasTurbineCycleSolver + SteamTurbineCycleSolver;
        // A10 adds ScramjetCycleSolver; A11 adds RbccCycleSolver (sub-step 1e capstone).
        // Wave 1 PR-4 (sub-step 1a.5, 2026-05-05) adds PulsejetCycleSolver.
        // Wave 2 (issue #428, 2026-05-06) adds TurbopropCycleSolver (PR #430) + TurboshaftCycleSolver.
        // Each new entry is appended here when the implementation lands.
        return new Dictionary<AirbreathingEngineKind, IAirbreathingCycleSolver>
        {
            [AirbreathingEngineKind.Ramjet]       = new RamjetCycleSolver(),
            [AirbreathingEngineKind.Turbojet]     = new TurbojetCycleSolver(),
            [AirbreathingEngineKind.Turbofan]     = new TurbofanCycleSolver(),
            [AirbreathingEngineKind.Scramjet]     = new ScramjetCycleSolver(),
            [AirbreathingEngineKind.Rbcc]         = new RbccCycleSolver(),
            [AirbreathingEngineKind.GasTurbine]   = new GasTurbineCycleSolver(),
            [AirbreathingEngineKind.SteamTurbine] = new SteamTurbineCycleSolver(),
            [AirbreathingEngineKind.Pulsejet]     = new PulsejetCycleSolver(),
            [AirbreathingEngineKind.Turboprop]    = new TurbopropCycleSolver(),
            [AirbreathingEngineKind.Turboshaft]   = new TurboshaftCycleSolver(),
            // Sprint A.W3 — Liquid Air Cycle Engine (LACE). Hybrid air-
            // breathing / rocket: LH₂ precooler liquefies captured air;
            // liquid air + LH₂ burn rocket-style.
            [AirbreathingEngineKind.LiquidAirCycle] = new LaceCycleSolver(),
            // Sprint A.W4 — Rotating Detonation Engine. Pressure-gain
            // combustion via azimuthally-propagating CJ detonation waves
            // in an annular combustor.
            [AirbreathingEngineKind.RotatingDetonation] = new RotatingDetonationCycleSolver(),
        };
    }
}
