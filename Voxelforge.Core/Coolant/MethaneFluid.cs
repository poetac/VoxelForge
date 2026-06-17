// MethaneFluid.cs — Supercritical methane thermophysical properties.
//
// Ported from the original inline tables in CoolantProperties.cs, now
// accessed through the ICoolantFluid abstraction. Behaviour is
// identical to the pre-refactor implementation — the regression test
// suite locks numeric output.
//
// Source: NIST REFPROP tabulation at P = 10 MPa.
// Critical point: T_crit = 190.56 K, P_crit = 4.599 MPa.
// Pseudocritical at 10 MPa: T_pc ≈ 210 K (shifts ≈ +3.3 K per +1 MPa).
//
// Density P-correction: linear above 250 K (gas-like branch); liquid-
// like below 250 K is treated incompressible.

namespace Voxelforge.Coolant;

/// <summary>
/// Supercritical methane thermophysical-property model for the
/// LOX/CH4 regen path, implementing <see cref="ICoolantFluid"/> through
/// <see cref="TabulatedCoolantFluid"/>. The legacy/canonical voxelforge
/// coolant — pre-refactor this lived inline in CoolantProperties.cs;
/// the regression suite locks numeric output bit-for-bit.
/// <para>
/// Source: NIST REFPROP tabulated at the reference pressure P = 10 MPa
/// over T = 100–700 K. Critical point: T_crit = 190.56 K,
/// P_crit = 4.599 MPa.
/// </para>
/// <para>
/// State validity: pseudocritical T at 10 MPa is T_pc ≈ 210 K and shifts
/// ≈ +3.3 K per +1 MPa; <see cref="IsInPseudocriticalRegion"/> returns
/// true within 25 K of T_pc and only when P ≥ P_crit. Density uses a
/// linear-P correction above 250 K (gas-like branch); below 250 K the
/// liquid-like branch is treated incompressible. <c>MaxBulkT_K = 900 K</c>
/// reflects pyrolysis onset; the metadata <c>ServiceLimitNote</c> reports
/// "Coking negligible below 700 K; pyrolysis above 900 K".
/// </para>
/// </summary>
internal sealed class MethaneFluid : TabulatedCoolantFluid
{
    /// <summary>Process-wide singleton. Tables are immutable; sharing one instance is safe.</summary>
    public static readonly MethaneFluid Instance = new();

    public override CoolantFluidMetadata Metadata => _meta;
    private static readonly CoolantFluidMetadata _meta = new(
        Key: "CH4",
        DisplayName: "Methane (supercritical)",
        CriticalT_K: 190.56,
        CriticalP_Pa: 4.599e6,
        MW_gmol: 16.04,
        MaxBulkT_K: 900.0,                // pyrolysis onset
        ServiceLimitNote: "Coking negligible below 700 K; pyrolysis above 900 K.");

    protected override double ReferencePressure_Pa => 10e6;
    protected override double LiquidLikeThreshold_K => 250.0;   // legacy constant from v1

    protected override double[] T_K_Axis => _T;
    protected override double[] Density_kgm3 => _Rho;
    protected override double[] Cp_Jkg => _Cp;
    protected override double[] Mu_uPaS => _Mu;
    protected override double[] K_WmK => _K;

    // NIST REFPROP at 10 MPa.
    private static readonly double[] _T = {
        100,  130,  160,  180,  195,  205,  210,  215,  220,  230,  245,
        260,  280,  300,  340,  400,  500,  600,  700 };
    private static readonly double[] _Rho = {
        435,  410,  370,  340,  310,  280,  255,  225,  190,  140,  110,
         92,   78,   68,   56,   46,   36,   29,   25 };
    private static readonly double[] _Cp = {
       3450, 3500, 3700, 4100, 5100, 6400, 7200, 6500, 5800, 4400, 3600,
       3200, 2950, 2850, 2750, 2700, 2700, 2720, 2760 };
    private static readonly double[] _Mu = {
        160,  120,   90,   75,   62,   55,   48,   42,   36,   28,   22,
         18,   16,   17,   19,   21,   25,   28,   32 };
    private static readonly double[] _K = {
       0.220, 0.195, 0.160, 0.135, 0.115, 0.095, 0.080, 0.068, 0.058, 0.050, 0.046,
       0.045, 0.046, 0.048, 0.054, 0.063, 0.078, 0.092, 0.106 };

    public override bool IsInPseudocriticalRegion(double T_K, double P_Pa)
    {
        if (P_Pa < Metadata.CriticalP_Pa) return false;
        double Tpc = 192.0 + (P_Pa / 1e6 - 5.0) * 3.3;
        return Math.Abs(T_K - Tpc) < 25.0;
    }
}
