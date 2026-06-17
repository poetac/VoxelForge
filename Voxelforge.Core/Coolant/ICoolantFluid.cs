// ICoolantFluid.cs — Common interface every regen-coolant fluid module
// must expose so the thermal solver can be propellant-pair-agnostic.
//
// The existing methane tables move into MethaneFluid.cs; new fluids
// (hydrogen, RP-1, etc.) implement the same contract and are registered
// in CoolantRegistry. The solver looks up the fluid key from the active
// propellant pair's metadata, so selecting LOX/H2 in the UI automatically
// switches the jacket-side physics.
//
// Design choices deliberately preserved from the original methane-only
// implementation:
//   • Tables are reference-pressure data with a lightweight log-P
//     correction on density. Cp/μ/k have no P correction (P-sensitivity
//     dominated by density near pseudocritical only; solver already flags
//     that region).
//   • Enthalpy is tabulated relative to a 100 K reference so the thermal
//     solver can invert (h → T) without iterating on Cp(T).
//   • Prandtl is derived from μ·Cp/k at lookup time (never tabulated
//     independently — avoids drift when the µ/Cp/k tables are edited).
//   • Pseudocritical flag is fluid-specific and folded into the solver's
//     warnings list.

namespace Voxelforge.Coolant;

/// <summary>
/// Lightweight metadata describing a coolant fluid — service limits,
/// phase-transition caveats, and user-facing notes. Consumed by the UI
/// and the thermal-solver warnings layer.
/// </summary>
public sealed record CoolantFluidMetadata(
    string Key,                         // "CH4", "H2", "RP-1", …
    string DisplayName,
    double CriticalT_K,
    double CriticalP_Pa,
    double MW_gmol,
    double MaxBulkT_K,                  // service limit above which physics breaks down
                                        // (RP-1 coking, LH2 H-embrittlement, etc.)
    string ServiceLimitNote);

public interface ICoolantFluid
{
    CoolantFluidMetadata Metadata { get; }

    /// <summary>
    /// Evaluate thermophysical state at (T, P). Implementations must
    /// clamp T to the table's valid range and apply whatever P
    /// correction they support internally.
    /// </summary>
    CoolantState GetState(double T_K, double P_Pa);

    /// <summary>
    /// Invert the enthalpy tabulation to recover T from accumulated
    /// enthalpy rise. The solver uses this so that h += q · dA / ṁ
    /// maps directly onto a new bulk temperature without iterating
    /// on Cp(T).
    /// </summary>
    double TemperatureFromEnthalpy(double H_Jkg);

    /// <summary>
    /// True when (T, P) lies in the pseudocritical transition region
    /// for this fluid. Useful for flagging low-confidence correlation
    /// predictions; not all fluids have a meaningful p-critical band
    /// at regen-chamber pressures.
    /// </summary>
    bool IsInPseudocriticalRegion(double T_K, double P_Pa);
}

/// <summary>
/// Base class that does the linear-interpolation boilerplate. Each
/// fluid module supplies its own monotonic T axis and per-property
/// arrays; the base handles lookup + monotonic enthalpy integration.
/// </summary>
public abstract class TabulatedCoolantFluid : ICoolantFluid
{
    public abstract CoolantFluidMetadata Metadata { get; }

    /// <summary>Reference pressure at which tables were tabulated.</summary>
    protected abstract double ReferencePressure_Pa { get; }

    protected abstract double[] T_K_Axis { get; }
    protected abstract double[] Density_kgm3 { get; }
    protected abstract double[] Cp_Jkg { get; }
    protected abstract double[] Mu_uPaS { get; }     // micro-Pa·s for compact tables
    protected abstract double[] K_WmK { get; }

    /// <summary>Apply a per-fluid P-correction to density. Default = linear-P.</summary>
    protected virtual double DensityPressureFactor(double T_K, double P_Pa)
        => P_Pa / ReferencePressure_Pa;

    /// <summary>Cut-off below which the density is treated as incompressible.</summary>
    protected virtual double LiquidLikeThreshold_K => 0.85 * Metadata.CriticalT_K;

    public abstract bool IsInPseudocriticalRegion(double T_K, double P_Pa);

    private double[]? _enthalpyKJkg;

    private double[] EnthalpyTable
    {
        get
        {
            if (_enthalpyKJkg == null) _enthalpyKJkg = BuildEnthalpyTable();
            return _enthalpyKJkg;
        }
    }

    private double[] BuildEnthalpyTable()
    {
        var T = T_K_Axis;
        var cp = Cp_Jkg;
        var h = new double[T.Length];
        h[0] = 0;
        for (int i = 1; i < T.Length; i++)
        {
            double dT = T[i] - T[i - 1];
            double CpAvg = 0.5 * (cp[i - 1] + cp[i]);
            h[i] = h[i - 1] + CpAvg * dT * 1e-3;    // kJ/kg
        }
        return h;
    }

    public CoolantState GetState(double T_K_in, double P_Pa_in)
    {
        var Ta = T_K_Axis;
        double T = Math.Clamp(T_K_in, Ta[0], Ta[^1]);
        double rho = Interp(Ta, Density_kgm3, T);
        double cp  = Interp(Ta, Cp_Jkg, T);
        double mu  = Interp(Ta, Mu_uPaS, T) * 1e-6;
        double k   = Interp(Ta, K_WmK, T);
        double h   = Interp(Ta, EnthalpyTable, T) * 1000.0;   // J/kg

        if (T > LiquidLikeThreshold_K)
            rho *= DensityPressureFactor(T, P_Pa_in);

        double Pr = mu * cp / Math.Max(k, 1e-6);
        return new CoolantState(T, P_Pa_in, rho, cp, mu, k, Pr, h);
    }

    public double TemperatureFromEnthalpy(double h_Jkg)
    {
        var Ta = T_K_Axis;
        var Hk = EnthalpyTable;
        double h_kJ = h_Jkg * 1e-3;
        if (h_kJ <= Hk[0]) return Ta[0];
        if (h_kJ >= Hk[^1]) return Ta[^1];
        for (int i = 0; i < Hk.Length - 1; i++)
        {
            if (h_kJ >= Hk[i] && h_kJ <= Hk[i + 1])
            {
                double t = (h_kJ - Hk[i]) / (Hk[i + 1] - Hk[i]);
                return Ta[i] + t * (Ta[i + 1] - Ta[i]);
            }
        }
        return Ta[^1];
    }

    protected static double Interp(double[] xs, double[] ys, double x)
    {
        if (x <= xs[0]) return ys[0];
        if (x >= xs[^1]) return ys[^1];
        for (int i = 0; i < xs.Length - 1; i++)
        {
            if (x >= xs[i] && x <= xs[i + 1])
            {
                double t = (x - xs[i]) / (xs[i + 1] - xs[i]);
                return ys[i] + t * (ys[i + 1] - ys[i]);
            }
        }
        return ys[^1];
    }
}
