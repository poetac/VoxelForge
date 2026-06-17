// PressurefedPresets.cs — Sprint 19 (2026-04-23):
//
// Preset factories for pressure-fed operating conditions. The existing
// OperatingConditions defaults work well for the regen-cooled turbopump-
// style 2-10 kN designs the project was originally scoped for; for
// small-thrust hardware (<500 N — hobbyist, cubesat, cold-gas RCS,
// amateur-rocketry) the defaults are overkill:
//
//   • 8 mm feed lines → huge for 0.1 kg/s flows
//   • 12 MPa coolant inlet pressure → absurd for a pressure-fed system
//     where tank pressure is probably 2-4 MPa
//   • 0 tank ullage pressure → stackup disabled (so no feasibility
//     signal on the pressure-fed path)
//
// `SmallThrust` returns an OperatingConditions that:
//   • Defaults the cycle to PressureFed (no turbopump sizing)
//   • Sets tank ullage to 1.5 × chamber pressure (conservative start)
//   • Blow-down mode ON with end-of-burn ≈ 1.1 × chamber pressure
//     (classic sport-class blow-down: ullage starts ~20% of tank and
//     drains to ~80-85% by EOB → pressure drop factor ~0.25-0.30;
//     picking 1.1×Pc end-of-burn gives a workable margin at EOB)
//   • 4 mm feed line (small-thrust appropriate)
//   • Coolant inlet pressure matches tank ullage (no pump)
//   • Valve Cv sized down to 0.3 (small-thrust bore)
//
// Callers then overlay any mission-specific fields (propellant pair,
// coolant temperature, material choice) on top via the `with` operator.

using Voxelforge.Combustion;
using Voxelforge.Optimization;

namespace Voxelforge.FeedSystem;

/// <summary>
/// Sprint 19 (2026-04-23): preset factories for pressure-fed
/// operating conditions. The defaults on <see cref="OperatingConditions"/>
/// are tuned for mid-thrust turbopump-cycle engines; these helpers
/// return a baseline that's sensible for small-thrust pressure-fed
/// hardware without forcing the user to override ~12 fields by hand.
/// </summary>
public static class PressurefedPresets
{
    /// <summary>
    /// Threshold below which the project considers a design "small
    /// thrust" — the point where turbopump hardware overhead exceeds
    /// the propellant mass for short missions. 500 N matches typical
    /// sport-class / amateur-rocketry thrusts and the Apollo RCS
    /// quads.
    /// </summary>
    public const double SmallThrustCeiling_N = 500.0;

    /// <summary>
    /// Build a small-thrust pressure-fed operating condition baseline.
    /// Suggested thrust range: 5-500 N. Above 500 N the defaults
    /// start to under-size the feed lines; callers should override
    /// <see cref="OperatingConditions.FeedLineDiameter_mm"/> and
    /// <see cref="OperatingConditions.MainValveCv"/> when scaling up.
    /// </summary>
    /// <param name="thrust_N">Target thrust (N). Scales feed geometry
    /// roughly as √(thrust) — 100 N uses 3 mm lines, 500 N uses 5 mm.</param>
    /// <param name="chamberPressure_Pa">Target chamber pressure (Pa).
    /// Defaults to 2.5 MPa — typical sport-class pressure-fed figure.
    /// Tank ullage is sized at 1.5 × this; the blow-down end-of-burn
    /// pressure at 1.1 × this.</param>
    /// <param name="propellantPair">Propellant pair. Defaults to
    /// LOX/CH4 to match the project's primary reference pair.</param>
    public static OperatingConditions SmallThrust(
        double thrust_N,
        double chamberPressure_Pa = 2.5e6,
        PropellantPair propellantPair = PropellantPair.LOX_CH4)
    {
        if (thrust_N <= 0)
            throw new System.ArgumentOutOfRangeException(nameof(thrust_N),
                "thrust must be positive");
        if (chamberPressure_Pa <= 0)
            throw new System.ArgumentOutOfRangeException(nameof(chamberPressure_Pa),
                "chamber pressure must be positive");

        // Feed-line diameter scales roughly as √(thrust / reference)
        // where reference is 500 N at 5 mm. Clamped to a 2-8 mm band
        // so the design stays physically plausible across 5-2000 N.
        double scale = System.Math.Sqrt(thrust_N / SmallThrustCeiling_N);
        double lineDia_mm = System.Math.Clamp(5.0 * scale, 2.0, 8.0);

        // Cv scales roughly linearly with flow area (≈ line area).
        // 0.3 Cv at 500 N is representative of a small ball valve.
        double valveCv = System.Math.Max(0.3 * scale, 0.1);

        // Tank ullage: 1.5 × target Pc (blow-down start point).
        double tankUllage_Pa = 1.5 * chamberPressure_Pa;

        // Blow-down end: 1.1 × target Pc — gives a workable margin at
        // end-of-burn after the stackup's line/injector losses
        // (typically 0.25-0.35 × Pc for a tuned small-thrust design).
        double blowDownEnd_Pa = 1.1 * chamberPressure_Pa;

        return new OperatingConditions
        {
            Thrust_N                = thrust_N,
            ChamberPressure_Pa      = chamberPressure_Pa,
            PropellantPair          = propellantPair,
            // PressureFed cycle — no turbopump sizing.
            EngineCycle             = EngineCycle.PressureFed,
            // Tank / feed-system stackup.
            TankUllagePressure_Pa     = tankUllage_Pa,
            BlowDownFinalPressure_Pa  = blowDownEnd_Pa,
            FeedLineLength_m          = 0.8,           // short run for small hardware
            FeedLineDiameter_mm       = lineDia_mm,
            MainValveCv               = valveCv,
            FilterStandard            = FilterStandard.Custom,
            FilterDeltaP_Pa           = 20_000.0,      // small-bore filter, lower ΔP
            // Coolant inlet matches tank ullage (no pump in the feed path).
            CoolantInletPressure_Pa   = tankUllage_Pa,
            CoolantInletTemp_K        = 150.0,         // LOX/CH4 default
            // Wall material + default MR left at the main-record defaults.
        };
    }
}
