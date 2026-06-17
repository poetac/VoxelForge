// PurgeFlowModel.cs — Pressure-drop model for GN₂ / GHe / GOX purge
// flows through the chamber's instrumentation-boss or plenum bores.
//
// MVP:
//   • Each purge port is a sharp-edge orifice with Cd = 0.60.
//   • Driving pressure = user-specified inletPressure_Pa.
//   • Backpressure = chamber pressure (worst case during start).
//   • Mass flow sized from Q = Cd·A·√(2·ρ·ΔP).
//   • PURGE_FLOW_INSUFFICIENT gate flags ports where actual flow is
//     less than the requested mass flow at the given inlet pressure.
//
// Purge fluids and densities (298 K, user-supplied inlet pressure):
//   • GN₂    ρ ≈ 1.165 × (P / 101325) kg/m³ at inlet
//   • Helium ρ ≈ 0.1664 × (P / 101325) kg/m³
//   • GOX    ρ ≈ 1.331 × (P / 101325) kg/m³   (treat as ideal gas)
//
// Above-300-m/s velocities are likely choked; MVP skips compressible
// correction and warns when the design approaches sonic.
//
// References:
//   Idelchik §4 (orifice discharge);
//   NASA SP-273 "Liquid Propellant Purge Systems" §2 (cryo start-up
//   purging with N₂ / He);
//   AIAA G-077 (propellant-servicing ground-support-equipment guide).

namespace Voxelforge.Coolant;

public enum PurgeFluid
{
    None = 0,
    GN2,
    Helium,
    GOX,
}

public enum PurgeLocation
{
    /// <summary>Oxidiser dome / back of injector — pre-fire inerting.</summary>
    InjectorDomeOx,
    /// <summary>Fuel dome / back of injector — pre-fire inerting.</summary>
    InjectorDomeFuel,
    /// <summary>Barrel region of the chamber — chilldown + post-fire.</summary>
    ChamberPrePurge,
    /// <summary>Nozzle exit — post-fire inert sweep.</summary>
    NozzleInertPurge,
}

/// <summary>
/// User specification for one purge port. MassFlow is the REQUESTED
/// flow the ground-side system must deliver; the model sizes the bore
/// and checks the pressure drop against the supply.
/// </summary>
public readonly record struct PurgePort(
    PurgeLocation Location,
    PurgeFluid    Fluid,
    double        MassFlow_kgs,
    double        InletPressure_Pa,
    double        BoreDiameter_mm);

public sealed record PurgePortResult(
    PurgePort Port,
    double ActualMassFlow_kgs,
    double DeltaP_Pa,
    double JetVelocity_ms,
    bool   MeetsRequestedFlow,
    string Notes);

public static class PurgeFlowModel
{
    /// <summary>Sharp-edge orifice discharge coefficient.</summary>
    public const double Cd = 0.60;

    /// <summary>Sonic warning threshold: 70 % of room-T sound speed.</summary>
    public const double SonicWarningFraction = 0.7;

    private static double FluidDensity_kgm3(PurgeFluid fluid, double pressure_Pa)
    {
        // Ideal-gas approximation at 298 K.
        double rhoAt1atm = fluid switch
        {
            PurgeFluid.GN2    => 1.165,
            PurgeFluid.Helium => 0.1664,
            PurgeFluid.GOX    => 1.331,
            _                 => 0.0,
        };
        if (rhoAt1atm <= 0) return 0;
        return rhoAt1atm * (pressure_Pa / 101_325.0);
    }

    public static PurgePortResult Evaluate(PurgePort port, double chamberPressure_Pa)
    {
        if (port.Fluid == PurgeFluid.None || port.InletPressure_Pa <= chamberPressure_Pa)
        {
            return new PurgePortResult(
                Port: port,
                ActualMassFlow_kgs: 0,
                DeltaP_Pa: 0,
                JetVelocity_ms: 0,
                MeetsRequestedFlow: port.MassFlow_kgs <= 0,
                Notes: "Inlet pressure ≤ chamber pressure — port cannot flow against the gradient.");
        }

        double rho = FluidDensity_kgm3(port.Fluid, port.InletPressure_Pa);
        if (rho <= 0)
            return new PurgePortResult(
                port, 0, 0, 0, false,
                "Unknown purge fluid — no density available.");

        double dP = port.InletPressure_Pa - chamberPressure_Pa;
        double d_m = port.BoreDiameter_mm * 1e-3;
        double A = System.Math.PI * d_m * d_m / 4.0;
        double v = System.Math.Sqrt(2.0 * dP / rho);        // ideal
        double actualMdot = Cd * A * rho * v;               // Q = Cd · A · ρ · v

        // Sonic warning: a = √(γ·R·T). Room T, γ ≈ 1.4 for N₂, 1.66 He,
        // 1.4 GOX. Use 340 m/s as a conservative sonic threshold.
        const double soundSpeed_approx = 340.0;
        bool nearChoked = v > SonicWarningFraction * soundSpeed_approx;

        string notes = $"Driving ΔP = {dP / 1e6:F2} MPa; jet velocity = {v:F0} m/s.";
        if (nearChoked)
            notes += " Near-choked flow — ideal-gas formula is optimistic; add a compressible factor for flight hardware.";

        bool meets = port.MassFlow_kgs <= 0
                  || actualMdot >= port.MassFlow_kgs * 0.95;

        return new PurgePortResult(
            Port: port,
            ActualMassFlow_kgs: actualMdot,
            DeltaP_Pa: dP,
            JetVelocity_ms: v,
            MeetsRequestedFlow: meets,
            Notes: notes);
    }

    /// <summary>
    /// Run the model across a list of purge ports. Returns one result per
    /// input, preserving order. Empty / null input returns an empty array.
    /// </summary>
    public static PurgePortResult[] EvaluateAll(
        System.Collections.Generic.IReadOnlyList<PurgePort>? ports,
        double chamberPressure_Pa)
    {
        if (ports is null || ports.Count == 0) return System.Array.Empty<PurgePortResult>();
        var arr = new PurgePortResult[ports.Count];
        for (int i = 0; i < ports.Count; i++)
            arr[i] = Evaluate(ports[i], chamberPressure_Pa);
        return arr;
    }
}
