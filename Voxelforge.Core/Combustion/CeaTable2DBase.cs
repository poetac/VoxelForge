// CeaTable2DBase.cs — Sprint 35 / PH-4 (2026-04-25).
//
// 2-D bilinear (Pc × MR) propellant-table base. Replaces the pre-Sprint-35
// `CeaTableBase` 1-D-MR + analytical-log-Pc-correction scheme with real
// CEA data at four Pc anchors {3, 7, 15, 25} MPa per pair. The log-Pc
// correction was an empirical fit calibrated near Pc=7 MPa; it degraded
// outside ~3-20 MPa and was systematically wrong at extreme MR (the
// 1-D arrays themselves were hand-tuned and diverged from CEA by up
// to 16 % in T_c at MR=2.0 for LOX/CH4 — that's the bug PH-4 fixes).
//
// Data generation: tools/gen_propellant_tables.py runs NASA CEA via the
// rocketcea Python wrapper across (Pc × MR) for each pair, validates
// against the existing 1-D values at Pc=7 MPa, and emits the C# array
// literals pasted into each Lox*Table.cs concrete class.
//
// Bilinear interpolation pattern (binary-search bracketing + linear in
// each axis) follows the established RaoBellTable.cs template. C* is
// recomputed from (γ, MW, T_c) at lookup time so any future tweak to
// the table data automatically stays thermodynamically consistent.
//
// PH-30 interaction: this is a frozen-equilibrium-chamber table.
// PropellantState.IsFrozen = true on output. Downstream callers may
// pass through EquilibriumCorrection.Correct (which sets IsFrozen
// false and noops on subsequent calls — see Sprint 38a).

namespace Voxelforge.Combustion;

/// <summary>
/// Base class for 2-D (Pc, MR) bilinear CEA propellant tables.
/// Subclasses supply the four-anchor Pc grid (default below), the MR
/// axis, and the three (Pc-row × MR-col) data tables for T_c, γ, MW.
/// 1-D Prandtl + IspVac arrays are kept per-MR-only (no Pc dependence
/// in the existing data; PH-4 didn't widen that scope).
/// </summary>
public abstract class CeaTable2DBase : IPropellantTable
{
    public abstract PropellantPair Pair { get; }
    public PropellantPairMetadata Metadata => PropellantPairs.GetMeta(Pair);

    /// <summary>Chamber-pressure anchor grid in Pa. Default = 4 anchors at 3, 7, 15, 25 MPa.</summary>
    protected virtual double[] PcGrid_Pa => DefaultPcGrid_Pa;
    private static readonly double[] DefaultPcGrid_Pa = { 3.0e6, 7.0e6, 15.0e6, 25.0e6 };

    /// <summary>Monotonic MR axis.</summary>
    protected abstract double[] MR { get; }

    /// <summary>Stagnation temperature [K], indexed [iPc, iMr].</summary>
    protected abstract double[,] TcTable_K { get; }
    /// <summary>Specific-heat ratio γ, indexed [iPc, iMr].</summary>
    protected abstract double[,] GammaTable { get; }
    /// <summary>Mean molecular weight [kg/kmol], indexed [iPc, iMr].</summary>
    protected abstract double[,] MwTable { get; }

    /// <summary>Prandtl number per MR (no Pc dependence in existing data).</summary>
    protected abstract double[] Prandtl { get; }
    /// <summary>Vacuum Isp at ε=40 per MR [s] (display/estimate only).</summary>
    protected abstract double[] IspVac_ref { get; }

    public PropellantState GetState(double mixtureRatio, double chamberPressure_Pa)
    {
        var meta = Metadata;
        double[] mrAxis = MR;
        double[] pcAxis = PcGrid_Pa;

        // Clamp inputs to the table envelope; bilinear interpolation
        // outside the grid would extrapolate without physical basis.
        double mr = System.Math.Clamp(mixtureRatio, mrAxis[0], mrAxis[^1]);
        double pc = System.Math.Clamp(chamberPressure_Pa, pcAxis[0], pcAxis[^1]);

        var (iMr, tMr) = BracketingFraction(mrAxis, mr);
        var (iPc, tPc) = BracketingFraction(pcAxis, pc);

        double Tc    = Bilinear(TcTable_K,  iPc, tPc, iMr, tMr);
        double gamma = Bilinear(GammaTable, iPc, tPc, iMr, tMr);
        double MW    = Bilinear(MwTable,    iPc, tPc, iMr, tMr);

        // Prandtl + Isp stay 1-D — existing data has no Pc-dependence
        // worth modelling at PH-4's scope; can be widened in a future
        // sprint if a real design surfaces sensitivity.
        double Pr  = Interp1D(mrAxis, Prandtl,    mixtureRatio);
        double Isp = Interp1D(mrAxis, IspVac_ref, mixtureRatio);

        // Derived: R, Cp, μ, C* — same formulae as legacy CeaTableBase
        // so the shape of PropellantState stays identical.
        double R     = PropellantTables.R_UNIVERSAL / MW;
        double Cp    = gamma / (gamma - 1.0) * R;
        double Gfun  = System.Math.Sqrt(gamma * System.Math.Pow(2.0 / (gamma + 1.0),
                          (gamma + 1.0) / (gamma - 1.0)));
        double cstar = System.Math.Sqrt(R * Tc) / System.Math.Max(Gfun, 1e-6);
        double mu    = 1.0e-4 * System.Math.Pow(Tc / 3500.0, 0.7);

        return new PropellantState(
            MixtureRatio:       mr,
            ChamberPressure_Pa: chamberPressure_Pa,
            ChamberTemp_K:      Tc,
            // PH-4 frozen-equilibrium-chamber table → GammaThroat = GammaChamber.
            // EquilibriumCorrection.Correct (PH-30 idempotent) can split them
            // when UseEquilibrium = true.
            GammaChamber:       gamma,
            GammaThroat:        gamma,
            MolecularWeight:    MW,
            SpecificGasConst:   R,
            Cp_Jkg:             Cp,
            Viscosity_PaS:      mu,
            Prandtl:            Pr,
            CStar_ms:           cstar,
            IspVacuum_s:        Isp,
            PropellantName:     meta.Name,
            // PH-30: frozen-table state. EquilibriumCorrection.Correct flips
            // this to false and noops on subsequent calls.
            IsFrozen:           true);
    }

    // ── Helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns (lo_index, fraction) such that
    ///   x ≈ grid[lo_index] + fraction × (grid[lo_index+1] - grid[lo_index])
    /// with fraction ∈ [0, 1]. Clamps to grid endpoints.
    /// </summary>
    private static (int lo, double t) BracketingFraction(double[] grid, double x)
    {
        if (x <= grid[0])  return (0, 0.0);
        if (x >= grid[^1]) return (grid.Length - 2, 1.0);
        int idx = System.Array.BinarySearch(grid, x);
        if (idx >= 0)
        {
            // Exact hit on a knot — bracket as (idx, 0) unless we're at the last knot.
            return idx == grid.Length - 1 ? (idx - 1, 1.0) : (idx, 0.0);
        }
        int hi = ~idx;
        int lo = hi - 1;
        return (lo, (x - grid[lo]) / (grid[hi] - grid[lo]));
    }

    /// <summary>
    /// Bilinear interpolation in a 2-D table indexed [iRow, iCol].
    /// <paramref name="iRow"/>/<paramref name="tRow"/> bracket the row axis
    /// (Pc); <paramref name="iCol"/>/<paramref name="tCol"/> bracket the
    /// column axis (MR).
    /// </summary>
    private static double Bilinear(double[,] tbl, int iRow, double tRow, int iCol, double tCol)
    {
        double v00 = tbl[iRow,     iCol];
        double v01 = tbl[iRow,     iCol + 1];
        double v10 = tbl[iRow + 1, iCol];
        double v11 = tbl[iRow + 1, iCol + 1];
        double v0 = v00 + tCol * (v01 - v00);
        double v1 = v10 + tCol * (v11 - v10);
        return v0 + tRow * (v1 - v0);
    }

    /// <summary>
    /// 1-D linear interpolation on monotonically-increasing xs. Same
    /// algorithm as the legacy CeaTableBase.Interp; kept here so the
    /// 2-D table base is self-contained.
    /// </summary>
    protected static double Interp1D(double[] xs, double[] ys, double x)
    {
        if (x <= xs[0])  return ys[0];
        if (x >= xs[^1]) return ys[^1];
        int idx = System.Array.BinarySearch(xs, x);
        if (idx >= 0) return ys[idx];
        int hi = ~idx;
        int lo = hi - 1;
        double t = (x - xs[lo]) / (xs[hi] - xs[lo]);
        return ys[lo] + t * (ys[hi] - ys[lo]);
    }
}
