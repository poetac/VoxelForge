// LoxHydrogenTable.cs — 2-D CEA bilinear table for LOX / liquid hydrogen.
//
// Sprint 35 / PH-4 (2026-04-25): swapped from 1-D + log-Pc correction
// to 2-D bilinear (Pc × MR) with real CEA equilibrium-chamber data.
// Notes on numbers:
//   - Peak C* near MR=3.5–4.0 (hydrogen-rich), peak Isp near MR=4.5–6.0.
//   - MW rises from ~8 (H2-rich) to ~14 (near stoichiometric).
//   - Low MW ⇒ very high C* (2300–2500 m/s) — ~30 % higher than kerosene.
//
// MR axis: 3.0 to 7.0 in 0.5 steps (9 points). Stoichiometric ≈ 7.94;
// most engines run fuel-rich (MR 4.5–6.5) for high Isp + wall margins.
// Pc axis: {3, 7, 15, 25} MPa — covers RL-10 (3.1) through SSME (20.6).
//
// Source: NASA CEA via rocketcea (LOX vs GH2 chamber lookup), generated
// 2026-04-25 by tools/gen_propellant_tables.py. Cross-validated against:
//   - Sutton & Biblarz 9e Table 5-5
//   - Huzel & Huang AIAA Vol. 147 § 4
//
// IMPLEMENTATION LIMIT: the coolant-property module only implements
// methane at present. The thermal solver will use methane properties even
// if this pair is selected. Treat predicted wall T / ΔP as **highly
// indicative only** until Coolant/HydrogenProperties.cs is written.

namespace Voxelforge.Combustion;

/// <summary>
/// 2-D bilinear CEA propellant-table for LOX / liquid hydrogen (LH2)
/// implementing <see cref="IPropellantTable"/> through
/// <see cref="CeaTable2DBase"/>. Looked up over (mixture-ratio,
/// chamber-pressure) and returns equilibrium-chamber Tc, gamma, MW plus
/// 1-D Prandtl + reference vacuum Isp.
/// <para>
/// Source: NASA CEA via rocketcea (LOX vs GH2 chamber lookup,
/// auto-generated 2026-04-25 by <c>tools/gen_propellant_tables.py</c>) —
/// equilibrium chamber, not frozen flow. Cross-validated against
/// Sutton &amp; Biblarz 9e Table 5-5 and Huzel &amp; Huang (AIAA Vol. 147 §4).
/// </para>
/// <para>
/// Validity: MR axis 3.0 to 7.0 in 0.5 steps (9 points; stoichiometric
/// MR ≈ 7.94, real engines run fuel-rich at MR 4.5–6.5 for high Isp +
/// wall margin); Pc anchors {3, 7, 15, 25} MPa covering RL-10 (3.1) to
/// SSME (20.6). Peak C* near MR=3.5–4.0 (hydrogen-rich); peak Isp near
/// MR=4.5–6.0; MW rises from ~8 (H2-rich) to ~14 (near stoichiometric).
/// </para>
/// <para>
/// Limitations: Prandtl + reference Isp are 1-D in MR only (no Pc
/// dependence within PH-4's scope); the returned
/// <see cref="PropellantState"/> is tagged <c>IsFrozen = false</c>.
/// Implementation note carried forward from the 2026-04-26 LH2-feasibility
/// session: the regen coolant-property module fully implements LH2 via
/// <see cref="Coolant.HydrogenFluid"/>, so thermal predictions for LOX/H2
/// are now first-class (no longer methane-substituted).
/// </para>
/// </summary>
internal sealed class LoxHydrogenTable : CeaTable2DBase
{
    /// <summary>Process-wide singleton. Tables are immutable; sharing one instance is safe.</summary>
    public static readonly LoxHydrogenTable Instance = new();

    /// <summary>Identifies this table as the LOX/H2 entry in the propellant registry.</summary>
    public override PropellantPair Pair => PropellantPair.LOX_H2;

    protected override double[]   MR          => _mr;
    protected override double[,]  TcTable_K   => _tc;
    protected override double[,]  GammaTable  => _gamma;
    protected override double[,]  MwTable     => _mw;
    protected override double[]   Prandtl     => _pr;
    protected override double[]   IspVac_ref  => _ispVac40;

    // ── MR axis (9 points, unchanged from pre-PH-4) ─────────────────
    private static readonly double[] _mr =
        { 3.0, 3.5, 4.0, 4.5, 5.0, 5.5, 6.0, 6.5, 7.0 };

    // ── 2-D tables — auto-generated 2026-04-25 ──────────────────────

    private static readonly double[,] _tc =
    {
        { 2631.5, 2861.3, 3046.4, 3192.6, 3305.6, 3389.9, 3449.6, 3488.7, 3510.9 },  // Pc =  3.0 MPa
        { 2643.9, 2887.5, 3089.6, 3253.6, 3383.5, 3482.8, 3554.9, 3603.1, 3631.0 },  // Pc =  7.0 MPa
        { 2651.9, 2905.6, 3121.4, 3300.8, 3446.5, 3560.7, 3645.7, 3704.0, 3738.5 },  // Pc = 15.0 MPa
        { 2656.0, 2915.2, 3139.0, 3328.1, 3484.3, 3608.9, 3703.5, 3769.6, 3809.4 },  // Pc = 25.0 MPa
    };

    private static readonly double[,] _gamma =
    {
        {  1.2139,  1.1900,  1.1712,  1.1570,  1.1465,  1.1389,  1.1336,  1.1301,  1.1281 },  // Pc =  3.0 MPa
        {  1.2202,  1.1979,  1.1794,  1.1647,  1.1534,  1.1449,  1.1388,  1.1347,  1.1322 },  // Pc =  7.0 MPa
        {  1.2245,  1.2039,  1.1861,  1.1714,  1.1597,  1.1505,  1.1436,  1.1389,  1.1359 },  // Pc = 15.0 MPa
        {  1.2268,  1.2073,  1.1902,  1.1757,  1.1638,  1.1543,  1.1469,  1.1417,  1.1383 },  // Pc = 25.0 MPa
    };

    private static readonly double[,] _mw =
    {
        {  8.034,  8.993,  9.916, 10.800, 11.642, 12.440, 13.193, 13.901, 14.564 },  // Pc =  3.0 MPa
        {  8.043,  9.015,  9.957, 10.864, 11.730, 12.554, 13.331, 14.060, 14.741 },  // Pc =  7.0 MPa
        {  8.049,  9.030,  9.987, 10.913, 11.803, 12.651, 13.452, 14.204, 14.903 },  // Pc = 15.0 MPa
        {  8.052,  9.039, 10.004, 10.942, 11.847, 12.711, 13.530, 14.298, 15.011 },  // Pc = 25.0 MPa
    };

    // ── 1-D Prandtl + IspVac (no Pc dependence in PH-4 scope) ───────

    private static readonly double[] _pr =
        { 0.48, 0.49, 0.50, 0.51, 0.52, 0.53, 0.54, 0.55, 0.55 };

    private static readonly double[] _ispVac40 =
        { 446, 452, 454, 452, 449, 443, 436, 428, 420 };
}
