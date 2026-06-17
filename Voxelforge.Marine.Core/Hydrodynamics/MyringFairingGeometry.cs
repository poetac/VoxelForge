// MyringFairingGeometry.cs — parametric Myring (1976) nose/tail fairing geometry.
//
// Computes the radial profile r(x) for a three-part axisymmetric hull:
//   (1) Myring nose    x ∈ [0, l_n]
//   (2) Cylindrical mid-body  x ∈ [l_n, l_n + l_m]
//   (3) Myring tail    x ∈ [l_n + l_m, L]
//
// Source: Myring, D. F. (1976). "A theoretical study of body drag in
// subcritical axisymmetric flow." Aeronautical Quarterly 27(3), 186-194.
//
// Profile equations:
//   Nose:  r(x) = (D/2) × [1 − (1 − x/l_n)^n]^(1/n)    n = 2.0
//   Tail:  r(ξ) = (D/2) × (1 − ξ^m)^p                   m = 1.5, p = 0.5
//          where ξ = (x − (L − l_t)) / l_t ∈ [0, 1]
//   Mid:   r = D/2  (constant)
//
// External volume integrated numerically at 200 stations (trapezoidal).
// Wetted area uses the Myring panel-area approximation:
//   S_wet ≈ π × D × (l_n/3 + l_m + l_t × 0.7)

using System;

namespace Voxelforge.Marine.Hydrodynamics;

/// <summary>
/// Computed fairing geometry for a Myring-parameterised hull.
/// Passed downstream to <see cref="HoernerDragSolver"/> and
/// <see cref="HydrostaticEquilibrium"/>.
/// </summary>
public sealed record FairingGeometry(
    double Length_m,
    double Diameter_m,
    double NoseLength_m,
    double TailLength_m,
    double MidBodyLength_m,
    double WettedArea_m2,
    double ExternalVolume_m3);

/// <summary>
/// Computes Myring (1976) fairing geometry from a <see cref="MarineDesign"/>.
/// </summary>
public static class MyringFairingGeometry
{
    private const double NoseExponent = 2.0;    // Myring n
    private const double TailExponentM = 1.5;   // Myring m
    private const double TailExponentP = 0.5;   // Myring p
    private const int IntegrationStations = 200;

    /// <summary>
    /// Compute fairing geometry for the supplied design.
    /// </summary>
    public static FairingGeometry Compute(MarineDesign design)
    {
        if (design is null) throw new ArgumentNullException(nameof(design));
        design.ValidateSelf();

        double l   = design.Length_m;
        double r   = design.Diameter_m / 2.0;
        double ln  = design.NoseLength_m;
        double lt  = design.TailLength_m;
        double lm  = design.MidBodyLength_m;

        // Wetted area (Myring panel-area approximation)
        double sWet = Math.PI * design.Diameter_m * (ln / 3.0 + lm + lt * 0.7);

        // External volume — numerical trapezoidal integration over 200 stations
        double volume = IntegrateVolume(l, r, ln, lt, lm);

        return new FairingGeometry(
            Length_m:        l,
            Diameter_m:      design.Diameter_m,
            NoseLength_m:    ln,
            TailLength_m:    lt,
            MidBodyLength_m: lm,
            WettedArea_m2:   sWet,
            ExternalVolume_m3: volume);
    }

    /// <summary>
    /// Radial profile r(x) at axial station x [m].
    /// </summary>
    internal static double RadiusAt(double x, double totalLength, double radius,
                                     double noseLen, double tailLen)
    {
        if (x <= noseLen)
        {
            // Myring nose: r(x) = R × [1 − (1 − x/ln)^n]^(1/n)
            double xi = x / noseLen;
            return radius * Math.Pow(1.0 - Math.Pow(1.0 - xi, NoseExponent), 1.0 / NoseExponent);
        }

        double tailStart = totalLength - tailLen;
        if (x >= tailStart)
        {
            // Myring tail: r(ξ) = R × (1 − ξ^m)^p, ξ = (x − tailStart) / tailLen
            double xi = (x - tailStart) / tailLen;
            xi = Math.Clamp(xi, 0.0, 1.0);
            return radius * Math.Pow(1.0 - Math.Pow(xi, TailExponentM), TailExponentP);
        }

        // Mid-body: constant radius
        return radius;
    }

    private static double IntegrateVolume(double l, double r, double ln, double lt, double lm)
    {
        // Trapezoidal rule, N+1 points → N intervals
        double dx = l / IntegrationStations;
        double sum = 0.0;
        double prevR = RadiusAt(0, l, r, ln, lt);
        double prevArea = Math.PI * prevR * prevR;

        for (int i = 1; i <= IntegrationStations; i++)
        {
            double x = i * dx;
            double ri = RadiusAt(x, l, r, ln, lt);
            double area = Math.PI * ri * ri;
            sum += (prevArea + area) * 0.5 * dx;
            prevArea = area;
        }

        return sum;
    }
}
