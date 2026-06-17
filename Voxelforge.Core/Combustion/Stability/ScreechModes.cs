// ScreechModes.cs — High-frequency acoustic-mode frequency estimation.
//
// Screech is the high-frequency (~1–10 kHz) combustion instability
// family driven by chamber acoustic modes coupling to the combustion
// response. The first step in screening a design is computing the
// natural mode frequencies assuming a cylindrical chamber with sound
// speed c = √(γ·R·T_c).
//
// Modes analyzed here (standard cylindrical-acoustics result; Harrje &
// Reardon NASA SP-194, 1972, Ch. 3):
//
//   L1   (1st longitudinal):   f = c / (2 · L_c)
//   T1   (1st tangential):     f = 1.841 · c / (π · D_c)
//   T2   (2nd tangential):     f = 3.054 · c / (π · D_c)
//
// The T-mode coefficients are the first two non-zero roots of
// J₁'(x) = 0 (derivative of the Bessel J₁), representing tangential
// standing waves.
//
// Preliminary-design fidelity: this is a pure-geometry-and-gas
// calculation — no injector-response, no damping, no Crocco n-τ
// coupling. It provides the frequencies a designer needs to cross-
// reference against injector response or external absorber tuning.
// STOP short of n-τ per project scope.

namespace Voxelforge.Combustion.Stability;

public readonly record struct ScreechModeResult(
    double SoundSpeed_ms,
    double L1_Hz,
    double T1_Hz,
    double T2_Hz);

public static class ScreechModes
{
    /// <summary>First non-zero root of J₁′(x) = 0 — 1st tangential mode.</summary>
    public const double BesselCoef_T1 = 1.841;

    /// <summary>Second non-zero root of J₁′(x) = 0 — 2nd tangential mode.</summary>
    public const double BesselCoef_T2 = 3.054;

    /// <summary>
    /// Speed of sound in the chamber gas c = √(γ·R·T_c).
    /// R is specific (not universal) here: R = R_u / MW.
    /// </summary>
    public static double SoundSpeed(double gamma, double specificGasConst, double chamberTemp_K)
        => System.Math.Sqrt(System.Math.Max(gamma * specificGasConst * chamberTemp_K, 0.0));

    /// <summary>
    /// Compute L1, T1, T2 frequencies for a cylindrical barrel of
    /// length <paramref name="chamberLength_m"/> and diameter
    /// <paramref name="chamberDiameter_m"/>.
    /// </summary>
    public static ScreechModeResult Evaluate(
        double gamma, double specificGasConst, double chamberTemp_K,
        double chamberLength_m, double chamberDiameter_m)
    {
        double c = SoundSpeed(gamma, specificGasConst, chamberTemp_K);
        double L = System.Math.Max(chamberLength_m, 1e-6);
        double D = System.Math.Max(chamberDiameter_m, 1e-6);

        double fL1 = c / (2.0 * L);
        double fT1 = BesselCoef_T1 * c / (System.Math.PI * D);
        double fT2 = BesselCoef_T2 * c / (System.Math.PI * D);

        return new ScreechModeResult(
            SoundSpeed_ms: c,
            L1_Hz: fL1,
            T1_Hz: fT1,
            T2_Hz: fT2);
    }
}
