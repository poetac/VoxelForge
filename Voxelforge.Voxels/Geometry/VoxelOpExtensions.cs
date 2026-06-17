// VoxelOpExtensions.cs — Extension helpers that pair PicoGK `Voxels`
// boolean ops with the temporary-Voxels disposal pattern.
//
// The naive
//     target.BoolSubtract(new Voxels(impl, bounds));
// pattern leaks the temporary's OpenVDB grid because the `Voxels` rvalue
// is never disposed. Each leak is substantial native memory; a single
// chamber build hits ~24 of these sites and UI iterative-refinement
// compounds the bleed across runs. Use `BoolSubtractTemp` / `BoolAddTemp`
// at every new call site that voxelises an implicit just to feed one
// boolean op against an existing accumulator.

using PicoGK;

namespace Voxelforge.Geometry;

public static class VoxelOpExtensions
{
    /// <summary>
    /// Voxelise <paramref name="impl"/> with <paramref name="bounds"/>, run
    /// `BoolSubtract` against <paramref name="target"/>, then dispose the
    /// temporary. Equivalent to
    /// <code>var v = new Voxels(impl, bounds); target.BoolSubtract(v); (v as IDisposable)?.Dispose();</code>
    /// collapsed into one call so a missed dispose can't slip through.
    /// </summary>
    public static void BoolSubtractTemp(this Voxels target, IImplicit impl, BBox3 bounds)
    {
        // PicoGK 2.0 follow-on: may be retirable — check if BoolSubtract now auto-disposes the temp Voxels
        var temp = LibraryScope.MakeVoxels(impl, bounds);
        target.BoolSubtract(temp);
        (temp as System.IDisposable)?.Dispose();
    }

    /// <summary>
    /// Voxelise <paramref name="impl"/> with <paramref name="bounds"/>, run
    /// `BoolAdd` against <paramref name="target"/>, then dispose the
    /// temporary.
    /// </summary>
    public static void BoolAddTemp(this Voxels target, IImplicit impl, BBox3 bounds)
    {
        // PicoGK 2.0 follow-on: may be retirable — check if BoolAdd now auto-disposes the temp Voxels
        var temp = LibraryScope.MakeVoxels(impl, bounds);
        target.BoolAdd(temp);
        (temp as System.IDisposable)?.Dispose();
    }
}
