// RefrigerationSuperheatGuardRegressionTests.cs — regression guard for the
// refrigeration negative-COP bug (red-team round-2 finding). PicoGK-free → runs
// on the Linux CI 'core' leg (uses InternalsVisibleTo for the internal types).
//
// RefrigerationSolver applies a linear COP penalty superheatPenalty =
// 1 − 0.002·SuperheatDepth_K (and a boost 1 + 0.006·SubcoolingDepth_K), but the
// depths were validated only ≥ 0 — no upper bound. Past ~500 K the penalty goes
// ≤ 0, so the cooling COP and cold-side heat removal flip sign (a "refrigerator"
// that adds heat) while the design still validates. Unlike the sibling Wave-2
// add-ons (PV/Battery bound their extra fields), refrigeration did not. Both
// depths are now capped at a generous physical ceiling.

using Voxelforge.Refrigeration;
using Xunit;

namespace Voxelforge.Core.Tests;

public sealed class RefrigerationSuperheatGuardRegressionTests
{
    private static RefrigerationDesign Design(double superheat = 0.0, double subcool = 0.0)
        => new(
            RefrigerationMode.Cooling,
            Refrigerant.R134a,
            ColdReservoirTemperature_K: 270.0,
            HotReservoirTemperature_K:  300.0,
            CompressorPowerInput_W:     1000.0)
        {
            SuperheatDepth_K  = superheat,
            SubcoolingDepth_K = subcool,
        };

    [Fact]
    public void ExcessiveSuperheat_Throws_RatherThanInvertingCop()
    {
        // Old code: penalty = 1 − 0.002·600 = −0.2 → negative cooling COP, and
        // the design validated fine. Now rejected.
        Assert.Throws<System.ArgumentException>(() => Design(superheat: 600.0).ValidateSelf());
    }

    [Fact]
    public void ExcessiveSubcooling_Throws()
        => Assert.Throws<System.ArgumentException>(() => Design(subcool: 600.0).ValidateSelf());

    [Fact]
    public void RealisticSuperheatAndSubcooling_StillSolvePositiveCop()
    {
        // Typical band (≤ ~30 K) must remain valid and produce a positive,
        // finite COP — the guard must not over-reject real designs.
        var r = RefrigerationSolver.Solve(Design(superheat: 10.0, subcool: 10.0));
        Assert.True(r.CoolingCop > 0.0 && double.IsFinite(r.CoolingCop),
            $"cooling COP should be positive and finite; got {r.CoolingCop}");
        Assert.True(r.ColdSideHeatRemoval_W > 0.0,
            $"cold-side heat removal should be positive; got {r.ColdSideHeatRemoval_W}");
    }
}
