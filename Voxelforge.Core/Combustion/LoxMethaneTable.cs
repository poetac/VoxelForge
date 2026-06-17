// LoxMethaneTable.cs — 2-D CEA bilinear table for LOX / liquid methane.
//
// Sprint 35 / PH-4 (2026-04-25): swapped from 1-D + log-Pc correction
// to 2-D bilinear (Pc × MR) with real CEA equilibrium-chamber data.
// Pre-PH-4 hand-tuned values diverged from CEA by up to 16 % on T_c at
// MR=2.0; the new tables are CEA-faithful (regenerate via
// `python tools/gen_propellant_tables.py`).
//
// MR axis: 2.0 to 5.0 in 0.25 steps (13 points) — covers fuel-rich
// (MR < 4) through stoichiometric (MR_stoich ≈ 4.0) to ox-rich.
// Pc axis: {3, 7, 15, 25} MPa — span typical LRE band.
// Per-cell uncertainty vs a fresh CEA run: ±0.1 % (data IS CEA).
//
// Source: NASA CEA via rocketcea, generated 2026-04-25 by
// tools/gen_propellant_tables.py. Cross-validated against:
//   - Sutton & Biblarz, "Rocket Propulsion Elements" 9e Table 5-5
//   - Haidn, "Advanced Rocket Engines", VKI LS 2008-05
//   - Bradford et al., JPC 2006, AIAA 2006-4708

namespace Voxelforge.Combustion;

/// <summary>
/// 2-D bilinear CEA propellant-table for LOX / liquid methane (CH4)
/// implementing <see cref="IPropellantTable"/> through
/// <see cref="CeaTable2DBase"/>. Looked up over (mixture-ratio,
/// chamber-pressure) and returns equilibrium-chamber Tc, gamma, MW
/// plus 1-D Prandtl + reference vacuum Isp.
/// <para>
/// Source: NASA CEA via rocketcea (auto-generated 2026-04-25 by
/// <c>tools/gen_propellant_tables.py</c>) — equilibrium chamber, not
/// frozen flow. Cross-validated against Sutton &amp; Biblarz 9e Table 5-5,
/// Haidn (VKI LS 2008-05) and Bradford et al. (AIAA 2006-4708).
/// </para>
/// <para>
/// Validity: MR axis 2.00 to 5.00 in 0.25 steps (13 points; stoichiometric
/// MR ≈ 4.0); Pc anchors {3, 7, 15, 25} MPa covering the typical LRE band.
/// Per-cell agreement with a fresh CEA run is ±0.1 % (the data IS CEA).
/// </para>
/// <para>
/// Limitations: Prandtl + reference Isp are 1-D in MR only — no Pc
/// dependence within PH-4's scope. <see cref="CeaTable2DBase"/> tags the
/// returned <see cref="PropellantState"/> as
/// <c>IsFrozen = false</c>; downstream code that needs frozen-flow values
/// must apply its own correction. Callers reach this table through
/// <see cref="PropellantTables.Lookup"/>; <c>Instance</c> is exposed for
/// the global <see cref="PropellantPair.LOX_CH4"/> registry only.
/// </para>
/// </summary>
internal sealed class LoxMethaneTable : CeaTable2DBase
{
    /// <summary>Process-wide singleton. Tables are immutable; sharing one instance is safe.</summary>
    public static readonly LoxMethaneTable Instance = new();

    /// <summary>Identifies this table as the LOX/CH4 entry in the propellant registry.</summary>
    public override PropellantPair Pair => PropellantPair.LOX_CH4;

    protected override double[]   MR          => _mr;
    protected override double[,]  TcTable_K   => _tc;
    protected override double[,]  GammaTable  => _gamma;
    protected override double[,]  MwTable     => _mw;
    protected override double[]   Prandtl     => _pr;
    protected override double[]   IspVac_ref  => _ispVac40;

    // ── MR axis ─────────────────────────────────────────────────────
    // 13 points: 2.00, 2.25, …, 5.00 — unchanged from pre-PH-4.
    private static readonly double[] _mr =
        { 2.00, 2.25, 2.50, 2.75, 3.00, 3.25, 3.50, 3.75, 4.00, 4.25, 4.50, 4.75, 5.00 };

    // ── 2-D tables — auto-generated 2026-04-25 ──────────────────────
    // Indexed [iPc, iMr]. Pc anchors: {3, 7, 15, 25} MPa.
    // CEA equilibrium chamber, rocketcea 1.2.3.

    private static readonly double[,] _tc =
    {
        { 2543.2, 2871.0, 3114.1, 3276.9, 3373.2, 3423.3, 3444.9, 3449.5, 3443.8, 3431.5, 3415.1, 3395.7, 3374.4 },  // Pc =  3.0 MPa
        { 2550.4, 2894.3, 3161.7, 3350.6, 3467.9, 3530.8, 3558.6, 3565.3, 3559.3, 3545.5, 3526.7, 3504.5, 3480.0 },  // Pc =  7.0 MPa
        { 2555.0, 2910.1, 3196.9, 3409.6, 3548.6, 3626.0, 3661.2, 3670.6, 3664.6, 3649.2, 3628.0, 3602.8, 3575.0 },  // Pc = 15.0 MPa
        { 2557.2, 2918.4, 3216.5, 3444.6, 3599.3, 3688.3, 3729.6, 3741.3, 3735.4, 3719.0, 3695.9, 3668.5, 3638.3 },  // Pc = 25.0 MPa
    };

    private static readonly double[,] _gamma =
    {
        {  1.2236,  1.1915,  1.1647,  1.1458,  1.1345,  1.1287,  1.1258,  1.1244,  1.1236,  1.1233,  1.1233,  1.1234,  1.1237 },  // Pc =  3.0 MPa
        {  1.2281,  1.1993,  1.1734,  1.1533,  1.1403,  1.1332,  1.1298,  1.1280,  1.1272,  1.1269,  1.1269,  1.1271,  1.1275 },  // Pc =  7.0 MPa
        {  1.2311,  1.2050,  1.1805,  1.1601,  1.1457,  1.1374,  1.1332,  1.1312,  1.1303,  1.1300,  1.1300,  1.1303,  1.1308 },  // Pc = 15.0 MPa
        {  1.2326,  1.2082,  1.1849,  1.1646,  1.1494,  1.1402,  1.1355,  1.1333,  1.1323,  1.1320,  1.1321,  1.1324,  1.1329 },  // Pc = 25.0 MPa
    };

    private static readonly double[,] _mw =
    {
        { 16.010, 17.251, 18.386, 19.393, 20.267, 21.027, 21.696, 22.294, 22.835, 23.331, 23.787, 24.208, 24.599 },  // Pc =  3.0 MPa
        { 16.021, 17.289, 18.468, 19.529, 20.452, 21.247, 21.938, 22.548, 23.096, 23.593, 24.048, 24.467, 24.854 },  // Pc =  7.0 MPa
        { 16.028, 17.314, 18.530, 19.638, 20.612, 21.445, 22.160, 22.784, 23.338, 23.837, 24.291, 24.706, 25.088 },  // Pc = 15.0 MPa
        { 16.031, 17.328, 18.564, 19.704, 20.713, 21.576, 22.311, 22.945, 23.504, 24.004, 24.456, 24.868, 25.246 },  // Pc = 25.0 MPa
    };

    // ── 1-D Prandtl + IspVac (no Pc dependence in PH-4 scope) ───────
    // Kept from pre-PH-4 tables — PH-4 didn't widen the data scope to
    // these fields. A future sprint can promote them to 2-D if a real
    // design surfaces sensitivity.

    private static readonly double[] _pr =
        { 0.555, 0.556, 0.558, 0.562, 0.566, 0.570, 0.573, 0.577, 0.580, 0.583, 0.585, 0.587, 0.588 };

    private static readonly double[] _ispVac40 =
        { 344, 351, 357, 361, 364, 365, 366, 365, 363, 360, 356, 351, 346 };
}
