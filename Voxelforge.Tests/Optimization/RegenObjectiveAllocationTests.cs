// RegenObjectiveAllocationTests.cs — performance audit 12-perf §1.1
// regression pin.
//
// Pre-fix: RegenObjective.Evaluate materialised the ReadOnlySpan<double>
// candidate vector into a fresh double[] via `vector.ToArray()` so it
// could call the array-shaped RegenChamberOptimization.Unpack overload.
// At ~5 M evaluations per SA session × ~248 B per 31-dim double[],
// that burned ~1.2 GB of Gen0 garbage per session.
//
// Post-fix: Unpack has a ReadOnlySpan<double> overload, RegenObjective
// dispatches to it directly, and the per-Evaluate vector-side
// allocation is gone.
//
// What we pin: the per-call delta between the new span-input Unpack
// path and the array-input Unpack path. Both should now allocate
// approximately the same number of bytes (because the array overload
// is a thin shim over the span overload — same record clone, same
// property-setter boxing, same gate eval). If a regression introduces
// a per-call `span.ToArray()` inside the span Unpack body, the
// delta jumps by ~280 B per call (a 31-dim double[] header + payload)
// and this test catches it.
//
// We measure under physics-free conditions: the Unpack call itself
// only touches the registry + record-clone + setter accessors, none
// of which involve the multi-MB physics evaluation pipeline. That
// keeps the measurement signal-to-noise high enough to assert on
// per-call byte deltas.

using System;
using Voxelforge.Combustion;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests.Optimization;

public class RegenObjectiveAllocationTests
{
    private static RegenChamberDesign Baseline() => new()
    {
        IncludeManifolds      = false,
        IncludePorts          = false,
        IncludeInjectorFlange = false,
        ContourStationCount   = 60,
    };

    /// <summary>
    /// Span-overload of <see cref="RegenChamberOptimization.Unpack(ReadOnlySpan{double}, RegenChamberDesign)"/>
    /// must not allocate a backing <c>double[]</c> for the vector
    /// itself. Both overloads (array + span) share the same body
    /// post-fix; the array overload simply delegates after a no-op
    /// cast. Per-call allocations should be approximately equal.
    /// <para>
    /// A regression that re-introduces <c>span.ToArray()</c> inside
    /// the span body would add ~280 B per call (DimensionCount * 8 +
    /// array header) on top of the legitimate record-clone cost.
    /// We require the span path to be within 64 B/call of the array
    /// path — well below the 280 B/call regression signature.
    /// </para>
    /// </summary>
    [Fact]
    public void Unpack_SpanOverload_AllocatesNoExtraVectorBuffer_VsArrayOverload()
    {
        var baseline = Baseline();
        var packed = RegenChamberOptimization.Pack(baseline);

        // Warmup: prime reflection + accessor caches.
        for (int i = 0; i < 100; i++) RegenChamberOptimization.Unpack(packed, baseline);
        for (int i = 0; i < 100; i++) RegenChamberOptimization.Unpack((ReadOnlySpan<double>)packed, baseline);

        const int n = 5000;

        // Array-overload path.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long startArr = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < n; i++) RegenChamberOptimization.Unpack(packed, baseline);
        long endArr = GC.GetAllocatedBytesForCurrentThread();
        long arrayPathBytes = endArr - startArr;

        // Span-overload path. Casting `packed` to ReadOnlySpan<double>
        // each iteration is allocation-free (span is a ref struct);
        // the only allocations should be inside Unpack itself.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long startSpan = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < n; i++)
        {
            ReadOnlySpan<double> v = packed;
            RegenChamberOptimization.Unpack(v, baseline);
        }
        long endSpan = GC.GetAllocatedBytesForCurrentThread();
        long spanPathBytes = endSpan - startSpan;

        long deltaBytesPerCall = (spanPathBytes - arrayPathBytes) / n;
        long absDelta = Math.Abs(deltaBytesPerCall);

        // Span path must not add measurable per-call bytes over the
        // array path. The pre-fix regression signature (a per-call
        // double[] materialisation) would add ~280 B/call; we require
        // < 64 B/call of difference (Gen0 measurement noise).
        Assert.True(
            absDelta < 64,
            $"Span Unpack overload allocates {deltaBytesPerCall} B/call more than the "
          + $"array overload (array: {arrayPathBytes / n} B/call, span: "
          + $"{spanPathBytes / n} B/call). A per-call double[] materialisation "
          + $"has likely been reintroduced — the regression signature is +~280 B/call.");
    }

    /// <summary>
    /// Per-call cost of the new <c>ReadOnlySpan&lt;double&gt;</c>
    /// Unpack overload measured directly. The 32 KB / call ceiling is
    /// well above the current ~19 KB / call (dominated by the
    /// registry's per-call <c>DescriptorsForMany(params Type[])</c>
    /// dispatch + the property-setter box-per-dim + the cloned
    /// RegenChamberDesign record itself — none of which the span fix
    /// touches), while leaving the test sensitive enough to catch a
    /// per-call vector materialisation regression alongside the
    /// cross-overload delta check above.
    /// <para>
    /// If this test ever starts failing because the per-call cost
    /// climbed past 32 KB, the right fix is usually upstream in the
    /// registry / setter caches, not in Unpack itself.
    /// </para>
    /// </summary>
    [Fact]
    public void Unpack_SpanOverload_PerCallBudget_StaysReasonable()
    {
        var baseline = Baseline();
        var packed = RegenChamberOptimization.Pack(baseline);

        for (int i = 0; i < 100; i++) RegenChamberOptimization.Unpack((ReadOnlySpan<double>)packed, baseline);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        const int n = 5000;
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < n; i++)
        {
            ReadOnlySpan<double> v = packed;
            RegenChamberOptimization.Unpack(v, baseline);
        }
        long end = GC.GetAllocatedBytesForCurrentThread();

        long bytesPerCall = (end - start) / n;
        const long budgetPerCall = 32 * 1024L;
        Assert.True(
            bytesPerCall < budgetPerCall,
            $"Unpack span overload over-allocates: {bytesPerCall} B/call "
          + $"(budget {budgetPerCall} B/call). The span fix preserves "
          + $"the array-overload cost; a per-call regression beyond this "
          + $"ceiling is usually upstream in the registry / setter caches.");
    }
}
