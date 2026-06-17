// PulsejetVariant.cs — geometry-variant discriminator for PulsejetCycleSolver.
//
// The two physically distinct pulsejet architectures share the same 0-D
// Humphrey-cycle model but differ in how fresh charge is aspirated:
//
//   Standard  — reed-valve assembly (spring-loaded flap grid) guides the
//               intake stroke. V-1 / Argus As 109-014 is the reference.
//               Higher volumetric efficiency (~14 %) because the valve
//               prevents reverse flow during the blowdown phase.
//
//   Valveless — no moving parts; both intake and exhaust openings are
//               always open. The Lockwood-Hiller U-tube is the canonical
//               design. Fresh charge is aspirated by reflected expansion
//               waves in the tailpipe (Foa 1960 §11.4). Lower volumetric
//               efficiency (~10 %) because the open intake allows partial
//               backflow. Simpler geometry, LPBF-friendly (no valve seat
//               or hinge clearance required).
//
// References:
//   Foa, J.V. 1960 Elements of Flight Propulsion, Wiley, §11.3-11.4.
//   Lockwood, V.E. 1950 NACA RM E50A04 (V-1 static tests, valved type).

namespace Voxelforge.Airbreathing.Cycles;

/// <summary>
/// Pulsejet geometry variant. Controls volumetric-efficiency selection in
/// <see cref="PulsejetCycleSolver.Solve"/>.
/// </summary>
public enum PulsejetVariant : byte
{
    /// <summary>
    /// Reed-valve (flap-valve) pulsejet. V-1 / Argus As 109-014 reference.
    /// Uses <see cref="PulsejetCycleSolver.StaticVolumetricEfficiency"/> = 0.14,
    /// calibrated against NACA RM E50A04 sea-level-static data.
    /// </summary>
    Standard = 0,

    /// <summary>
    /// Valveless (Lockwood-Hiller U-tube) pulsejet. Both ends open; no
    /// moving parts. Uses
    /// <see cref="PulsejetCycleSolver.ValvelessVolumetricEfficiency"/> = 0.10
    /// per Foa 1960 §11.4 typical range 8–12 %.
    /// </summary>
    Valveless = 1,
}
