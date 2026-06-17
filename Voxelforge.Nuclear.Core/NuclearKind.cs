// NuclearKind.cs — sub-variant discriminator for the nuclear thermal rocket pillar.

namespace Voxelforge.Nuclear;

/// <summary>
/// Sub-variant of the nuclear thermal rocket family.
/// Wave-1 covers <see cref="NervaSolidCore"/> only.
/// Wave-2+ extends to bimodal NTR and nuclear ramjet.
/// </summary>
public enum NuclearKind
{
    /// <summary>
    /// NERVA-class solid-core NTR. Reactor heats LH2 propellant through a
    /// channel matrix; UO2-cermet or graphite-composite fuel elements.
    /// Historical baseline: NRX-A6 ground test (Jackass Flats NV, 1969).
    /// </summary>
    NervaSolidCore = 0,

    /// <summary>
    /// Bimodal NTR — same reactor as <see cref="NervaSolidCore"/> but with
    /// a closed-cycle He Brayton gas loop coupled to the reactor for
    /// electric power generation alongside (or instead of) LH₂ thrust.
    /// NASA-class bimodal concepts (SP-100 / SAFE-400 derivative): the
    /// reactor produces both thrust (when LH₂ flows) and ~10–100 kWe of
    /// electric power. Sprint NU.W3. Wave-3.
    /// Design knobs:
    /// <see cref="NuclearThermalDesign.BimodalMode"/>,
    /// <see cref="NuclearThermalDesign.ElectricPowerTarget_kWe"/>,
    /// <see cref="NuclearThermalDesign.BraytonTurbineInletTemp_K"/>,
    /// <see cref="NuclearThermalDesign.BraytonHePressure_bar"/>,
    /// <see cref="NuclearThermalDesign.AlternatorRpm"/>.
    /// </summary>
    BimodalNtr = 1,
}

/// <summary>
/// Operating mode for a bimodal NTR design. Only meaningful when
/// <see cref="NuclearKind.BimodalNtr"/> is selected.
/// </summary>
public enum BimodalMode
{
    /// <summary>
    /// Pure thrust mode — LH₂ flows through the reactor; Brayton loop is
    /// idle. Behaviour is bit-identical to <see cref="NuclearKind.NervaSolidCore"/>.
    /// </summary>
    Thrust = 0,

    /// <summary>
    /// Pure electric mode — LH₂ flow is shut off; reactor heat drives the
    /// closed-cycle He Brayton loop only. No thrust output. Reactor power
    /// scaled to the design <see cref="NuclearThermalDesign.ElectricPowerTarget_kWe"/>.
    /// </summary>
    Electric = 1,

    /// <summary>
    /// Hybrid mode — both LH₂ thrust and Brayton electric output
    /// simultaneously. Cluster value at lower-end NTR throttle (~20 % of
    /// rated thermal power for thrust, balance for electric).
    /// </summary>
    Hybrid = 2,
}
