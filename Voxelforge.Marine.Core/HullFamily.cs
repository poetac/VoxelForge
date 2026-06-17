namespace Voxelforge.Marine;

/// <summary>Hull geometry family for the Marine pillar.</summary>
public enum HullFamily
{
    /// <summary>
    /// Myring (1976) three-part nose/cylinder/tail fairing — Wave-1 default.
    /// </summary>
    Myring = 0,

    /// <summary>
    /// Cylindrical mid-body with hemispherical endcaps — Wave-2 M2.
    /// S_wet = πDL; V_ext = (π/6)D³ + (π/4)D²(L−D).
    /// </summary>
    CylindricalHemi = 1,

    /// <summary>
    /// Hard-chine planing hull (Savitsky 1964 regime) — Wave-3 M.W3.
    /// Surface vehicle, not submerged. Bare-hull resistance from Savitsky's
    /// closed-form lift + drag fit on prismatic hard-chine forms; trim
    /// angle solved by lift-equals-weight equilibrium.
    /// </summary>
    Planing = 2,

    /// <summary>
    /// Displacement-mode round-bilge hull (Holtrop-Mennen 1984 regime) —
    /// Wave-3 Sprint M.W4. Surface vehicle, not submerged. Bridges the gap
    /// between the AUV (Wave-1/2, Fn ≲ 0.1) and Planing (Wave-3, Fn ≳ 1.0)
    /// regimes: applicable to ~Fn ∈ [0.05, 0.40] cargo / fishing /
    /// motor-vessel cluster. Resistance from a simplified parametric form
    /// of the Holtrop-Mennen 1984 polynomial fit: ITTC-1957 friction +
    /// form factor (1 + k₁) + Holtrop wave-making (simplified to the
    /// dominant c₁·exp(m·Fn^d) term).
    /// </summary>
    DisplacementSurface = 3,
}
