// MotorKind.cs — Sprint EM.W1 electric-motor sub-classifier.
//
// Wave-1 ships two cluster topologies that cover the dominant EV +
// industrial-servo + drone markets:
//
//   BrushlessDc — 3-phase BLDC with trapezoidal back-EMF; permanent-
//                 magnet rotor + electronic commutator. Drone, RC, and
//                 light-EV (e-bike / scooter) workhorse.
//   PermanentMagnetSync — 3-phase PMSM with sinusoidal back-EMF +
//                         field-oriented control. EV traction (Tesla
//                         Model S, Nissan Leaf, Chevrolet Bolt) +
//                         industrial servo workhorse.
//
// Wave-2+ will add induction (Tesla Model S R-class), switched-
// reluctance, and axial-flux disc topologies.

namespace Voxelforge.ElectricMotor;

/// <summary>
/// Sub-classification of electric motor (Sprint EM.W1).
/// </summary>
internal enum MotorKind
{
    /// <summary>Degenerate sentinel.</summary>
    None = 0,

    /// <summary>3-phase brushless DC (BLDC) — trapezoidal back-EMF, electronic commutator.</summary>
    BrushlessDc = 1,

    /// <summary>3-phase permanent-magnet synchronous (PMSM) — sinusoidal back-EMF + FOC.</summary>
    PermanentMagnetSynchronous = 2,
}
