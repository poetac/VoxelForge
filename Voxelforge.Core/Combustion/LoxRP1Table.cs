// LoxRP1Table.cs — 2-D CEA bilinear table for LOX / RP-1 kerosene.
//
// Sprint 35 / PH-4 (2026-04-25): swapped from 1-D + log-Pc correction
// to 2-D bilinear (Pc × MR) with real CEA equilibrium-chamber data.
// Modern engines: Merlin 1D Pc = 9.7 MPa; heritage F-1 Pc = 7 MPa.
// MR axis: 2.0 to 2.8 in 0.1 steps (10 points). Stoichiometric ≈ 3.4 but
// real engines run fuel-rich (MR ≈ 2.3–2.6) to keep coking under control
// and hold T_c below CuCrZr's limit.
//
// CRITICAL operating constraint: **RP-1 coking**. Above ~550 K bulk the
// kerosene polymerises and plates out on the coolant channels. Structural
// and thermal results will NOT flag this — the user must keep coolant
// outlet T below ~600 K or switch to methane.
//
// Source: NASA CEA via rocketcea (LOX vs RP-1), generated 2026-04-25 by
// tools/gen_propellant_tables.py. Cross-validated against:
//   - Sutton 9e Table 5-5; Huzel & Huang AIAA Vol. 147
//   - Wadel, NASA TM-107084 (1997)
//   - Merlin-1D published Tc data
//
// IMPLEMENTATION LIMIT: Coolant/CoolantProperties is methane-only today.
// Treat thermal predictions as sanity-checks, not design decisions, when
// the regen coolant is RP-1.

namespace Voxelforge.Combustion;

/// <summary>
/// 2-D bilinear CEA propellant-table for LOX / RP-1 kerosene
/// implementing <see cref="IPropellantTable"/> through
/// <see cref="CeaTable2DBase"/>. Looked up over (mixture-ratio,
/// chamber-pressure) and returns equilibrium-chamber Tc, gamma, MW plus
/// 1-D Prandtl + reference vacuum Isp.
/// <para>
/// Source: NASA CEA via rocketcea (LOX vs RP-1, auto-generated 2026-04-25
/// by <c>tools/gen_propellant_tables.py</c>) — equilibrium chamber, not
/// frozen flow. Cross-validated against Sutton 9e Table 5-5,
/// Huzel &amp; Huang (AIAA Vol. 147), Wadel (NASA TM-107084 1997) and
/// Merlin-1D published Tc data.
/// </para>
/// <para>
/// Validity: MR axis 2.00 to 2.80 in 0.10 steps (10 points;
/// stoichiometric MR ≈ 3.4, but real engines run fuel-rich at MR 2.3–2.6
/// to control coking and hold Tc below CuCrZr's limit); Pc anchors
/// {3, 7, 15, 25} MPa cover heritage F-1 (7.0) through Merlin-1D (9.7).
/// </para>
/// <para>
/// CRITICAL operating constraint — RP-1 coking. Above ~550 K bulk the
/// kerosene polymerises and plates out on coolant channels. The thermal
/// solver does NOT detect coking in this table; the user must keep
/// coolant outlet T_bulk below 600 K (or switch to methane). The
/// <see cref="Coolant.RP1Fluid"/> coolant module enforces a 600 K
/// MaxBulkT_K to surface a service-limit warning.
/// </para>
/// <para>
/// Limitations: Prandtl + reference Isp are 1-D in MR only (no Pc
/// dependence within PH-4's scope); the returned
/// <see cref="PropellantState"/> is tagged <c>IsFrozen = false</c>.
/// </para>
/// </summary>
internal sealed class LoxRP1Table : CeaTable2DBase
{
    /// <summary>Process-wide singleton. Tables are immutable; sharing one instance is safe.</summary>
    public static readonly LoxRP1Table Instance = new();

    /// <summary>Identifies this table as the LOX/RP-1 entry in the propellant registry.</summary>
    public override PropellantPair Pair => PropellantPair.LOX_RP1;

    protected override double[]   MR          => _mr;
    protected override double[,]  TcTable_K   => _tc;
    protected override double[,]  GammaTable  => _gamma;
    protected override double[,]  MwTable     => _mw;
    protected override double[]   Prandtl     => _pr;
    protected override double[]   IspVac_ref  => _ispVac40;

    // ── MR axis (10 points, unchanged from pre-PH-4) ────────────────
    private static readonly double[] _mr =
        { 2.00, 2.10, 2.20, 2.30, 2.40, 2.50, 2.56, 2.60, 2.70, 2.80 };

    // ── 2-D tables — auto-generated 2026-04-25 ──────────────────────

    private static readonly double[,] _tc =
    {
        { 3288.2, 3370.9, 3434.2, 3481.4, 3515.6, 3539.9, 3550.7, 3556.6, 3567.4, 3573.8 },  // Pc =  3.0 MPa
        { 3353.1, 3450.1, 3526.2, 3584.2, 3627.0, 3657.8, 3671.6, 3679.1, 3693.3, 3701.9 },  // Pc =  7.0 MPa
        { 3403.2, 3513.6, 3602.6, 3672.1, 3724.4, 3762.5, 3779.8, 3789.4, 3807.6, 3819.0 },  // Pc = 15.0 MPa
        { 3432.1, 3551.4, 3649.5, 3727.4, 3787.0, 3831.1, 3851.3, 3862.6, 3884.1, 3897.9 },  // Pc = 25.0 MPa
    };

    private static readonly double[,] _gamma =
    {
        {  1.1657,  1.1556,  1.1478,  1.1420,  1.1378,  1.1348,  1.1333,  1.1325,  1.1309,  1.1296 },  // Pc =  3.0 MPa
        {  1.1750,  1.1641,  1.1553,  1.1486,  1.1436,  1.1399,  1.1382,  1.1372,  1.1353,  1.1338 },  // Pc =  7.0 MPa
        {  1.1830,  1.1718,  1.1623,  1.1547,  1.1489,  1.1447,  1.1426,  1.1415,  1.1392,  1.1374 },  // Pc = 15.0 MPa
        {  1.1881,  1.1768,  1.1670,  1.1590,  1.1527,  1.1479,  1.1456,  1.1444,  1.1418,  1.1399 },  // Pc = 25.0 MPa
    };

    private static readonly double[,] _mw =
    {
        { 20.724, 21.224, 21.687, 22.114, 22.510, 22.879, 23.088, 23.223, 23.546, 23.852 },  // Pc =  3.0 MPa
        { 20.845, 21.376, 21.868, 22.322, 22.741, 23.128, 23.347, 23.488, 23.824, 24.139 },  // Pc =  7.0 MPa
        { 20.939, 21.499, 22.020, 22.502, 22.946, 23.354, 23.584, 23.731, 24.080, 24.406 },  // Pc = 15.0 MPa
        { 20.994, 21.573, 22.115, 22.617, 23.079, 23.504, 23.742, 23.895, 24.255, 24.590 },  // Pc = 25.0 MPa
    };

    // ── 1-D Prandtl + IspVac (no Pc dependence in PH-4 scope) ───────

    private static readonly double[] _pr =
        { 0.52, 0.52, 0.53, 0.53, 0.54, 0.54, 0.54, 0.55, 0.55, 0.55 };

    private static readonly double[] _ispVac40 =
        { 341, 345, 348, 351, 353, 354, 354, 354, 353, 352 };
}
