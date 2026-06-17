// OOB-11 (issue #340): standalone monoprop feasibility checks.
// Not integrated with GateRegistry (which is typed to RegenGenerationResult).
using Voxelforge.Combustion;
using System.Collections.Generic;

namespace Voxelforge.Optimization;

public static class MonopropGates
{
    // Practical Ir/Al2O3 catalyst service limit — above this Tc the bed
    // degrades rapidly and requires exotic precious-metal catalysts.
    public const double CatalystBedMaxTemp_K = 1700.0;

    public static void EvaluateAll(
        MonopropResult result,
        MonopropSpec spec,
        List<FeasibilityViolation> violations)
    {
        if (result.CatalystLoading_kgm2s > spec.CatalystLoadingLimit_kgm2s)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: "MONOPROP_CATALYST_OVERLOADED",
                Description:  $"Catalyst loading {result.CatalystLoading_kgm2s:F2} kg/(m²·s) "
                            + $"exceeds {spec.Name} limit {spec.CatalystLoadingLimit_kgm2s:F1} kg/(m²·s).",
                ActualValue:  result.CatalystLoading_kgm2s,
                Limit:        spec.CatalystLoadingLimit_kgm2s));
        }

        if (spec.Tc_K > CatalystBedMaxTemp_K)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: "MONOPROP_CHAMBER_TEMP_EXCEEDS_BED",
                Description:  $"{spec.Name} Tc {spec.Tc_K:F0} K exceeds practical Ir/Al2O3 "
                            + $"service limit {CatalystBedMaxTemp_K:F0} K; consider platinum catalyst.",
                ActualValue:  spec.Tc_K,
                Limit:        CatalystBedMaxTemp_K));
        }
    }
}
