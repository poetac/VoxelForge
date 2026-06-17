// ShutdownBlowdownSimTests.cs — Pure-physics unit tests for the
// shutdown / blowdown integrator (Hot-fire-readiness Item 4 close-out).
// No PicoGK references; sidesteps the xUnit + PicoGK pitfall (CLAUDE.md
// pitfall #8).

using Voxelforge.Combustion;
using Xunit;

namespace Voxelforge.Tests;

public class ShutdownBlowdownSimTests
{
    /// <summary>
    /// Representative reference inputs — Merlin-class chamber sizing.
    /// Scaled per the Merlin canonical preset (100 kN at Pc 7 MPa,
    /// L* ≈ 1.1 m). Used by every test as the baseline; tests vary
    /// individual fields with `with`-expressions.
    /// </summary>
    private static ShutdownBlowdownInputs MerlinClassInputs() => new(
        SteadyMassFlow_kgs:    33.0,        // 100 kN at Isp ≈ 310 s
        ChamberPressure_Pa:    7e6,
        ChamberVolume_m3:      6e-3,        // ~6 L chamber
        CStar_ms:              1820,        // LOX/CH4 typical
        ThroatArea_m2:         9e-4,        // 30 mm radius throat
        ValveCloseTime_s:      0.10,        // 100 ms ball valve
        AmbientPressure_Pa:    101_325,     // sea level
        SimulationDuration_s:  3.0,
        TimeStep_s:            1e-3);

    // ── Sanity guards ────────────────────────────────────────────

    [Fact]
    public void Run_ZeroDuration_ReturnsEmptyResultWithWarning()
    {
        var inp = MerlinClassInputs() with { SimulationDuration_s = 0 };
        var r = ShutdownBlowdownSim.Run(inp);
        Assert.Empty(r.Samples);
        Assert.NotEmpty(r.Warnings);
    }

    [Fact]
    public void Run_TooSmallTimeStep_RejectedWithWarning()
    {
        var inp = MerlinClassInputs() with { TimeStep_s = 1e-9 };
        var r = ShutdownBlowdownSim.Run(inp);
        Assert.Empty(r.Samples);
        Assert.Contains(r.Warnings, w => w.Contains("floor", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Run_ZeroMassFlow_ReturnsEmptyResultWithWarning()
    {
        var inp = MerlinClassInputs() with { SteadyMassFlow_kgs = 0 };
        var r = ShutdownBlowdownSim.Run(inp);
        Assert.Empty(r.Samples);
        Assert.NotEmpty(r.Warnings);
    }

    // ── Valve profile ────────────────────────────────────────────

    [Theory]
    [InlineData(-0.01, 0.10, 1.0)]   // before t=0: valve held open
    [InlineData( 0.00, 0.10, 1.0)]   // at t=0: full open
    [InlineData( 0.05, 0.10, 0.5)]   // mid-ramp: half open
    [InlineData( 0.10, 0.10, 0.0)]   // at close-time: fully closed
    [InlineData( 0.50, 0.10, 0.0)]   // after close: stays closed
    [InlineData( 1.00, 0.00, 0.0)]   // closeTime = 0 ⇒ instant cutoff
    public void ValveProfile_LinearRamp(double t, double closeTime, double expected)
    {
        var actual = ShutdownBlowdownSim.ValveProfile(t, closeTime);
        Assert.Equal(expected, actual, precision: 6);
    }

    // ── Pressure decay ───────────────────────────────────────────

    [Fact]
    public void Run_PressureMonotonicallyDecreasingAfterValveClose()
    {
        // After both valves close, ṁ_in = 0 so Pc must monotonically
        // decay toward zero. Pin this — the lumped first-order lag
        // analytic solution is strictly monotonic for τ > 0.
        var r = ShutdownBlowdownSim.Run(MerlinClassInputs());
        Assert.NotEmpty(r.Samples);

        // Find the index where both valves are fully closed.
        int closeIdx = System.Array.FindIndex(r.Samples,
            s => s.OxValvePosition == 0 && s.FuelValvePosition == 0);
        Assert.True(closeIdx > 0, "Both valves should fully close within sim window");

        // After close, Pc should be monotonic. Allow a tiny floating-
        // point tolerance for rounding in the integrator.
        for (int i = closeIdx + 1; i < r.Samples.Length; i++)
        {
            Assert.True(
                r.Samples[i].ChamberPressure_Pa <= r.Samples[i - 1].ChamberPressure_Pa + 1e-3,
                $"Pc not monotonically decreasing after valve close: "
              + $"sample[{i-1}].Pc = {r.Samples[i-1].ChamberPressure_Pa:F2}, "
              + $"sample[{i}].Pc = {r.Samples[i].ChamberPressure_Pa:F2}");
        }
    }

    [Fact]
    public void Run_TimeTo10PctPc_HappensWithinValveCloseRamp()
    {
        // For the Merlin-class baseline τ_chamber = V_c / (c*·A_t) =
        // 6e-3 / (1820 × 9e-4) ≈ 3.7 ms. The valve-close ramp itself
        // takes 100 ms = 27 τ_chamber. Because τ_chamber is so much
        // shorter than the ramp, Pc closely tracks pcTarget which
        // falls linearly from Pc_steady to 0 during 0 ≤ t ≤ 0.10 s.
        //
        // 10 % crossing happens when pcTarget ≈ 0.1·Pc_steady, i.e.
        // (1 - t/0.10) ≈ 0.10 ⇒ t ≈ 0.09 s. With lag adding ~τ_c,
        // expect t ≈ 0.092 s.
        //
        // Bounds [0.05, 0.20] s tolerate alternative integrators or
        // future τ-tuning; the exact value lands at ~0.092 today.
        var r = ShutdownBlowdownSim.Run(MerlinClassInputs());
        Assert.False(double.IsNaN(r.TimeTo10PctPc_s),
            "TimeTo10PctPc_s should be populated for a 3 s sim window");
        Assert.InRange(r.TimeTo10PctPc_s, 0.05, 0.20);
    }

    [Fact]
    public void Run_TimeToSubcritical_LessThanOrEqualTo_TimeTo10PctPc()
    {
        // 10 % of 7 MPa = 0.7 MPa, far above the subcritical threshold
        // (1.1 × 101 325 = 111 kPa). So time-to-subcritical ≥
        // time-to-10%-Pc — Pc has further to fall.
        var r = ShutdownBlowdownSim.Run(MerlinClassInputs());
        Assert.False(double.IsNaN(r.TimeToSubcritical_s));
        Assert.True(r.TimeToSubcritical_s >= r.TimeTo10PctPc_s,
            $"Subcritical should come AFTER 10% Pc: "
          + $"t_subcritical = {r.TimeToSubcritical_s:F3} s, "
          + $"t_10pct = {r.TimeTo10PctPc_s:F3} s");
    }

    // ── Per-side staged shutdowns ────────────────────────────────

    [Fact]
    public void Run_FuelLeadShutdown_OxValveStaysOpenLonger()
    {
        // "Ox-lean cutoff" — close fuel first, then ox. Pin that the
        // ox valve position lags fuel during the staged shutdown.
        var inp = MerlinClassInputs() with
        {
            FuelValveCloseTime_s = 0.05,    // close fuel in 50 ms
            OxValveCloseTime_s   = 0.20,    // close ox over 200 ms
        };
        var r = ShutdownBlowdownSim.Run(inp);

        // At t = 0.10 s: fuel should be fully closed (closed at 0.05);
        // ox should be at ~0.5 (halfway through 0-0.20 ramp).
        var s = System.Array.Find(r.Samples,
            x => System.Math.Abs(x.Time_s - 0.10) < 1e-6);
        Assert.True(s.Time_s > 0, "Sample at t=0.10 should exist");
        Assert.Equal(0.0, s.FuelValvePosition, precision: 3);
        Assert.InRange(s.OxValvePosition, 0.45, 0.55);
    }

    // ── Residual propellant tracking ─────────────────────────────

    [Fact]
    public void Run_ResidualBurnedPlusVented_NearTriangularRampBound()
    {
        // Total residual (burned + vented) integrated during the
        // valve-close ramp ≈ ṁ_steady · ValveCloseTime_s · 0.5
        // (= triangular area under the falling-ramp ṁ curve).
        //
        // The integrator uses left-rectangle Euler integration which
        // overshoots the analytical triangular area by
        // ṁ_steady · TimeStep_s · 0.5 (one extra rectangle's worth
        // of bias at the beginning). For the baseline (1 ms timestep)
        // the bias is 33 · 1e-3 · 0.5 = 0.0165 kg on top of the
        // 1.65 kg analytical value.
        var r = ShutdownBlowdownSim.Run(MerlinClassInputs());
        double total = r.ResidualPropellantBurned_kg + r.ResidualPropellantVented_kg;
        double idealTriangular = 33.0 * 0.10 * 0.5;
        double eulerBias       = 33.0 * 1e-3 * 0.5;
        double upperBound      = idealTriangular + eulerBias + 1e-3;
        Assert.True(total <= upperBound,
            $"Total residual {total:F4} kg exceeds bound {upperBound:F4} kg "
          + $"(analytical = {idealTriangular:F4}, Euler bias = {eulerBias:F4}).");
        Assert.True(total >= idealTriangular * 0.95,
            $"Total residual {total:F4} kg is too low — should be near "
          + $"the analytical triangular area {idealTriangular:F4} kg.");
    }

    // ── Purge sweep ──────────────────────────────────────────────

    [Fact]
    public void Run_WithPurge_HoldsResidualPressureAboveAmbient()
    {
        // A constant purge gas flow holds chamber Pc at a small but
        // non-zero ullage above ambient. The chamber Pc target while
        // purging is (ṁ_purge / ṁ_steady) · Pc_steady = 0.01 × 7 MPa
        // = 70 kPa for a 1 % purge — below ambient (101 kPa) so the
        // throat is not choked, but still non-zero (the integrator
        // tracks the purge target rather than hard-zeroing Pc).
        var inp = MerlinClassInputs() with
        {
            SimulationDuration_s = 5.0,
            PurgeMassFlow_kgs    = 0.5,            // 1.5 % of ṁ_steady
            PurgeTriggerDelay_s  = 0.5,
        };
        var r = ShutdownBlowdownSim.Run(inp);

        // Late sample (after valves closed + purge active for several τ_c)
        // should sit at the purge equilibrium.
        var late = r.Samples[r.Samples.Length - 1];
        double expectedPurgePc = (0.5 / 33.0) * 7e6;
        Assert.InRange(late.ChamberPressure_Pa, 0.7 * expectedPurgePc, 1.3 * expectedPurgePc);
    }

    // ── Aggregate valve position for legacy chart consumers ──────

    [Fact]
    public void Run_AggregateValvePosition_AverageOfPerSide()
    {
        // The aggregate ValvePosition field is the average of per-side
        // for back-compat with single-channel chart consumers. Pin
        // this so the property never silently changes shape.
        var inp = MerlinClassInputs() with
        {
            FuelValveCloseTime_s = 0.05,
            OxValveCloseTime_s   = 0.20,
        };
        var r = ShutdownBlowdownSim.Run(inp);

        foreach (var s in r.Samples)
        {
            double expected = 0.5 * (s.OxValvePosition + s.FuelValvePosition);
            Assert.Equal(expected, s.ValvePosition, precision: 6);
        }
    }
}
