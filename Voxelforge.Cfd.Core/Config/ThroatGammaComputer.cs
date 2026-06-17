// ThroatGammaComputer.cs — Derives a distinct GammaThroat by querying PropellantTables
// at the isentropic throat static pressure, activating the CpPolynomialFitter C.3 path.
//
// Background (issue #454): EquilibriumCorrection.Correct() applies the same gammaFactor
// to GammaChamber and GammaThroat, so they are always equal after correction.
// CpPolynomialFitter.Fit() detects this equality and returns IsFlatCp=true, meaning the
// C.3 polynomial improvement is never reached with stock PropellantState values.
//
// Fix: re-query PropellantTables at P* (isentropic critical pressure) to get the equilibrium
// γ that corresponds to the lower-temperature / lower-pressure throat conditions, then return
// a PropellantState with GammaThroat set to that value. The CEA table is parameterised by
// (Pc, MR); querying at P* gives the equilibrium γ at those conditions, which differs from
// the chamber γ because at lower Pc the equilibrium composition shifts slightly (less
// recombination of dissociation products → different γ). The table clamps inputs to its
// envelope (CeaTable2DBase lines 67-68), so out-of-bounds P* is handled safely.

using Voxelforge.Combustion;

namespace Voxelforge.Cfd.Config;

/// <summary>
/// Derives a <see cref="PropellantState"/> with a distinct
/// <see cref="PropellantState.GammaThroat"/> by querying <see cref="PropellantTables"/>
/// at the isentropic critical pressure P*.
/// </summary>
public static class ThroatGammaComputer
{
    /// <summary>
    /// Returns <paramref name="chamber"/> with <see cref="PropellantState.GammaThroat"/>
    /// replaced by the equilibrium γ from a <see cref="PropellantTables"/> lookup at the
    /// isentropic throat static pressure P* = P_c · (2/(γ+1))^(γ/(γ−1)).
    /// <para>
    /// When P* falls below the table's minimum Pc (e.g. ~3 MPa for LOX/CH4), the table
    /// clamps to the boundary and the result may equal the chamber γ — in that case
    /// <see cref="CpPolynomialFitter.Fit"/> will return <see cref="CpPolynomialResult.IsFlatCp"/>=true
    /// and the runner falls back gracefully to the Sprint C.2 frozen-γ path.
    /// </para>
    /// </summary>
    /// <param name="chamber">Chamber-conditions gas state.</param>
    /// <param name="pair">Propellant pair used for the table lookup.</param>
    public static PropellantState WithThroatGamma(PropellantState chamber, PropellantPair pair)
    {
        double g = chamber.GammaChamber;
        if (!double.IsFinite(g) || g <= 1.0)
            return chamber;

        double pThroat = IsentropicThroatPressure(chamber.ChamberPressure_Pa, g);
        if (!double.IsFinite(pThroat) || pThroat <= 0.0)
            return chamber;

        PropellantState throatState;
        try
        {
            throatState = PropellantTables.Lookup(pair, chamber.MixtureRatio, pThroat);
        }
        catch
        {
            return chamber;
        }

        return chamber with { GammaThroat = throatState.GammaChamber };
    }

    /// <summary>
    /// Computes the isentropic critical pressure P* = P_c · (2/(γ+1))^(γ/(γ−1)).
    /// </summary>
    public static double IsentropicThroatPressure(double chamberPressure_Pa, double gamma)
        => chamberPressure_Pa * Math.Pow(2.0 / (gamma + 1.0), gamma / (gamma - 1.0));
}
