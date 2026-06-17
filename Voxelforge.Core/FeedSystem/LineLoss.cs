// LineLoss.cs — Darcy–Weisbach friction loss on a straight propellant feed line.
//
// ΔP = f · (L / D) · ½·ρ·v²
//
// Friction factor via the Haaland explicit fit to Colebrook for the
// turbulent regime, laminar (Re < 2300) uses 64/Re:
//
//   1/√f = −1.8 · log10( (ε/D/3.7)^1.11 + 6.9/Re )
//
// Roughness ε defaults to 15 µm — representative of smooth commercial
// stainless tubing. Users wanting a different roughness will need a
// dedicated input; MVP keeps it hardcoded.

namespace Voxelforge.FeedSystem;

public static class LineLoss
{
    /// <summary>Absolute roughness ε (m) of stainless feed-line tubing, MVP constant.</summary>
    public const double Roughness_m = 1.5e-5;

    /// <summary>Viscosity (Pa·s) fallback when a real fluid lookup isn't available.</summary>
    public const double ViscosityFallback_PaS = 3e-4;

    public static double FrictionDP(
        double length_m, double dia_m,
        double massFlow_kgs, double density_kgm3,
        double viscosity_PaS = ViscosityFallback_PaS)
    {
        if (length_m <= 0 || dia_m <= 0 || massFlow_kgs <= 0) return 0.0;
        double rho = System.Math.Max(density_kgm3, 1e-3);
        double mu  = System.Math.Max(viscosity_PaS, 1e-6);
        double A = System.Math.PI * dia_m * dia_m / 4.0;
        double v = massFlow_kgs / (rho * A);
        double Re = rho * v * dia_m / mu;
        double f;
        if (Re < 2300)
        {
            f = 64.0 / System.Math.Max(Re, 1);
        }
        else
        {
            // Haaland explicit friction factor.
            double term = System.Math.Pow(Roughness_m / dia_m / 3.7, 1.11)
                        + 6.9 / Re;
            double inv = -1.8 * System.Math.Log10(System.Math.Max(term, 1e-12));
            f = 1.0 / System.Math.Max(inv * inv, 1e-6);
        }
        return f * (length_m / dia_m) * 0.5 * rho * v * v;
    }
}
