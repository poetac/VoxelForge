// ValveCv.cs — Main-valve ΔP from the industry-standard Cv (flow coefficient).
//
// The Cv metric is defined as the flow of 60 °F water (ρ = 1000 kg/m³,
// SG = 1) in US gpm that produces ΔP = 1 psi across the valve:
//
//   Q [gpm] = Cv · √(ΔP [psi] / SG)
//
// For arbitrary fluid and mass-flow:
//
//   ΔP [Pa] = (SG / Cv²) · Q² [gpm²] · 6895
//
// where SG = ρ / 1000 and 6895 converts psi → Pa.
//
// Typical Cv values:
//   0.5  —  small check valves
//   2.0  —  hand ball valve, 1/4" bore
//   10.0 —  1" full-port ball valve
//   ≥ 50 —  industrial shutoffs / test-stand main valves

namespace Voxelforge.FeedSystem;

/// <summary>
/// Industry-standard valve flow-coefficient (Cv) sizing — converts a
/// catalog Cv rating + an arbitrary fluid mass-flow + density into the
/// pressure drop the valve will impose on the line.
/// <para>
/// The <c>Cv</c> metric is defined as the flow of 60 °F water
/// (ρ = 1000 kg/m³, SG = 1) in US gpm that produces ΔP = 1 psi across
/// the valve:
/// </para>
/// <code>
///   Q [gpm] = Cv · √(ΔP [psi] / SG)
/// </code>
/// <para>
/// Solving for ΔP and converting units gives the implementation in
/// <see cref="DeltaP"/>:
/// </para>
/// <code>
///   ΔP [Pa] = (SG / Cv²) · Q² [gpm²] · 6895
/// </code>
/// <para>
/// Typical Cv values: 0.5 — small check valves; 2.0 — hand ball valve,
/// 1/4" bore; 10.0 — 1" full-port ball valve; ≥ 50 — industrial shutoffs
/// / test-stand main valves.
/// </para>
/// </summary>
public static class ValveCv
{
    private const double GpmPerM3PerS = 15850.3;    // 1 m³/s = 15850.3 US gpm
    private const double PsiPerPa     = 1.0 / 6894.76;

    /// <summary>
    /// Computes the pressure drop in Pa across a valve of the given
    /// flow coefficient at the supplied mass-flow + density. Returns 0
    /// when any input is non-positive (defensive guard for unsized
    /// valves / zero-flow startup states; matches the rest of the
    /// feed-system module's "no plumbing → no ΔP" convention).
    /// </summary>
    /// <param name="Cv_gpm_psi">Valve flow coefficient in US gpm/√psi (catalog units).</param>
    /// <param name="massFlow_kgs">Mass flow rate through the valve in kg/s.</param>
    /// <param name="density_kgm3">Fluid density at the valve in kg/m³.</param>
    /// <returns>Pressure drop in Pa; 0 for any non-positive input.</returns>
    public static double DeltaP(double Cv_gpm_psi, double massFlow_kgs, double density_kgm3)
    {
        if (Cv_gpm_psi <= 0 || massFlow_kgs <= 0 || density_kgm3 <= 0) return 0;
        double Q_m3s = massFlow_kgs / density_kgm3;
        double Q_gpm = Q_m3s * GpmPerM3PerS;
        double SG = density_kgm3 / 1000.0;
        // ΔP [psi] = SG · (Q / Cv)²
        double dP_psi = SG * (Q_gpm / Cv_gpm_psi) * (Q_gpm / Cv_gpm_psi);
        return dP_psi / PsiPerPa;   // → Pa
    }
}
