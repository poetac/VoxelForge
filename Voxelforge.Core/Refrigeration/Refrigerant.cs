// Refrigerant.cs — Sprint RFG.W1 working-fluid discriminator.
//
// Wave-1 ships four cluster representatives across the commercial
// HVAC + automotive + natural-refrigerant cluster:
//
//   R-134a:    Medium-T HVAC + automotive AC (1990s-2010s default).
//              GWP 1430. η_2nd-law ≈ 0.55.
//   R-410A:    Residential split AC + light commercial (2010s default).
//              GWP 2088. η_2nd-law ≈ 0.58.
//   R-1234yf:  Low-GWP automotive AC replacement for R-134a (post-2017).
//              GWP < 1. η_2nd-law ≈ 0.55.
//   R-744:     CO₂ transcritical — heat-pump water heaters (Sanden),
//              natural refrigerant. η_2nd-law ≈ 0.50 (lower because
//              the transcritical gas-cooler loses ε vs subcritical
//              condensation, but the high T_glide makes it ideal for
//              hot-water heating).
//
// Wave-2+ will add R-32 (residential), R-290 propane (small commercial),
// R-717 ammonia (industrial), and R-718 water (high-T).

namespace Voxelforge.Refrigeration;

/// <summary>
/// Working refrigerant for the vapor-compression cycle (Sprint RFG.W1).
/// </summary>
internal enum Refrigerant
{
    /// <summary>Degenerate sentinel.</summary>
    None = 0,

    /// <summary>R-134a (1,1,1,2-tetrafluoroethane). Medium-T HVAC default.</summary>
    R134a = 1,

    /// <summary>R-410A (R-32 + R-125 azeotrope). Residential split AC.</summary>
    R410A = 2,

    /// <summary>R-1234yf (2,3,3,3-tetrafluoropropene). Low-GWP R-134a replacement.</summary>
    R1234yf = 3,

    /// <summary>R-744 (CO₂ transcritical). Natural refrigerant for heat-pump water heaters.</summary>
    R744 = 4,
}
