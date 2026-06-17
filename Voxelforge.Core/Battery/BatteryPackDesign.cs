// BatteryPackDesign.cs — Sprint BP.W1 battery pack design record.
//
// Sized to bracket the Tesla Model 3 long-range pack cluster (96
// series × 46 parallel = 4416 NMC cells, ~ 82 kWh nominal). The pack
// is the series-parallel arrangement of identical chemistry cells; the
// design record carries the array topology + a single per-cell SoC.

using System;

namespace Voxelforge.Battery;

/// <summary>
/// Design parameters for a lithium-class battery pack (Sprint BP.W1
/// scaffold). Standalone — does not integrate with the
/// <c>IEngine&lt;,,&gt;</c> stack yet (deferred to a future BP.W2 sprint).
/// </summary>
/// <param name="Chemistry">Cell chemistry — drives OCV span + R_int + capacity per cell.</param>
/// <param name="CellsInSeries">N_series [-] — sets pack voltage.</param>
/// <param name="ParallelStrings">N_parallel [-] — sets pack capacity + current capability.</param>
/// <param name="StateOfCharge">Per-cell SoC ∈ [0, 1] [-] (uniform across the pack — a Wave-1 simplification).</param>
/// <param name="LoadCurrent_A">Pack-level discharge current I_pack [A]. Positive = discharge; negative = charge.</param>
internal sealed record BatteryPackDesign(
    BatteryChemistry Chemistry,
    int    CellsInSeries,
    int    ParallelStrings,
    double StateOfCharge,
    double LoadCurrent_A)
{
    /// <summary>Total cell count = N_series × N_parallel.</summary>
    public int TotalCellCount => CellsInSeries * ParallelStrings;

    /// <summary>
    /// Sprint BP.W2. Cell-stack temperature [°C]. Defaults to 25 °C
    /// (Standard Test Conditions; BP.W1 hard-coded). Drives capacity
    /// derating — Li-ion cluster loses ~ 0.5 %/°C below 0 °C and
    /// ~ 0.3 %/°C above 45 °C (Plett 2015 §2.5 + Wang 2011).
    /// </summary>
    public double CellTemperature_C { get; init; } = 25.0;

    /// <summary>Validate structural self-consistency of the design record.</summary>
    /// <exception cref="ArgumentException">When chemistry is None, any count
    /// is non-positive, or SoC is out of [0, 1].</exception>
    public void ValidateSelf()
    {
        if (Chemistry == BatteryChemistry.None)
            throw new ArgumentException(
                "Chemistry must be set (None sentinel is reserved).", nameof(Chemistry));
        if (CellsInSeries <= 0)
            throw new ArgumentException("CellsInSeries must be > 0.", nameof(CellsInSeries));
        if (ParallelStrings <= 0)
            throw new ArgumentException("ParallelStrings must be > 0.", nameof(ParallelStrings));
        if (StateOfCharge < 0.0 || StateOfCharge > 1.0)
            throw new ArgumentException(
                $"StateOfCharge must be in [0, 1]; got {StateOfCharge}.",
                nameof(StateOfCharge));
    }
}
