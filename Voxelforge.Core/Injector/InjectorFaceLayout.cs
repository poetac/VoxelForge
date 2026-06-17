// InjectorFaceLayout.cs — Pattern-layout library for placing N
// injector elements across the injector face. Consumed by
// ChamberVoxelBuilder's injector-orifice step and by any downstream
// analysis that needs per-element coordinates (stability screening,
// residence-time models, CFD IC seeding).
//
// Layouts
// ───────
//   Circular      — all N elements equally spaced on a single pitch
//                   circle. Matches the legacy behaviour exactly,
//                   so every existing design round-trips bit-identical.
//   Hexagonal     — closest-packed triangular grid clipped to the
//                   chamber radius. Native pattern for shear-coax
//                   injectors (F-1 / RS-25 heritage).
//   AnnularRows   — concentric rings (count derived from N so rings
//                   stay ≥ 6 elements to avoid pitch-circle degenerate
//                   cases). Canonical for swirl-coax patterns; Huzel
//                   & Huang §8.1 Fig. 8-3.
//
// Pattern selection is owned by the caller; AutoSeeder picks a default
// based on element type (Coax → Hex, ImpingingDoublet → Circular,
// Pintle → Central, Showerhead → Hex, Swirl → AnnularRows).

namespace Voxelforge.Injector;

/// <summary>
/// Chosen pattern for distributing <c>ElementCount</c> elements across
/// the injector face. Stored as an enum so the design record can
/// persist it without a custom JSON converter.
/// </summary>
public enum InjectorFaceLayout
{
    /// <summary>Legacy: all N on one pitch circle at <c>pitchRadius_mm</c>.</summary>
    Circular = 0,

    /// <summary>Closest-packed triangular grid clipped to the chamber radius.</summary>
    Hexagonal = 1,

    /// <summary>Concentric rings with element count per ring scaled by ring radius.</summary>
    AnnularRows = 2,

    /// <summary>
    /// Single element at the chamber axis. Used by Pintle (one pintle
    /// per chamber). Ignores <c>ElementCount</c> values &gt; 1.
    /// </summary>
    Central = 3,
}

/// <summary>
/// Pure-math layout generator. No PicoGK dependency; safe to call from
/// any thread; synchronous. Returns element centres in the (y, z)
/// face plane with x=0 at the injector face.
/// </summary>
public static class InjectorFaceLayoutGenerator
{
    /// <summary>
    /// Minimum elements per ring before we demote a ring to the next
    /// inner ring. 6 elements ⇒ 60° pitch, tight but workable.
    /// </summary>
    public const int MinElementsPerRing = 6;

    /// <summary>
    /// Emit element centre positions for the requested layout + count
    /// + pitch circle. Positions are returned as 2-tuples (y, z) —
    /// x is always 0 at the injector face and is added by callers.
    /// </summary>
    /// <param name="layout">Layout strategy.</param>
    /// <param name="elementCount">Requested count. AnnularRows + Hex
    /// may return slightly fewer if geometry forces it; Central always
    /// returns exactly one. Consumers must use the returned array's
    /// Length, not the requested count.</param>
    /// <param name="pitchRadius_mm">Outer pitch radius (mm) — the outer
    /// bound on element placement. Typically 0.60 × chamber radius.</param>
    /// <param name="chamberRadius_mm">Chamber inner radius (mm) — used
    /// for Hexagonal clipping. Ignored by Circular.</param>
    /// <param name="elementSpacing_mm">Centre-to-centre spacing between
    /// adjacent elements (mm). Only Hex uses it. 0 ⇒ derive from
    /// <paramref name="pitchRadius_mm"/> and the element count.</param>
    public static (double y_mm, double z_mm)[] PlaceElements(
        InjectorFaceLayout layout,
        int                elementCount,
        double             pitchRadius_mm,
        double             chamberRadius_mm,
        double             elementSpacing_mm = 0.0)
    {
        if (elementCount < 1) throw new ArgumentOutOfRangeException(nameof(elementCount));
        if (pitchRadius_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(pitchRadius_mm));
        if (chamberRadius_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(chamberRadius_mm));

        return layout switch
        {
            InjectorFaceLayout.Circular     => PlaceCircular(elementCount, pitchRadius_mm),
            InjectorFaceLayout.Central      => new[] { (0.0, 0.0) },
            InjectorFaceLayout.AnnularRows  => PlaceAnnularRows(elementCount, pitchRadius_mm),
            InjectorFaceLayout.Hexagonal    => PlaceHexagonal(elementCount, pitchRadius_mm,
                                                               chamberRadius_mm,
                                                               elementSpacing_mm),
            _ => PlaceCircular(elementCount, pitchRadius_mm),
        };
    }

    // ────────────────────── Circular ──────────────────────

    private static (double, double)[] PlaceCircular(int n, double pitchRadius_mm)
    {
        var result = new (double, double)[n];
        for (int k = 0; k < n; k++)
        {
            double theta = 2.0 * Math.PI * k / n;
            result[k] = (pitchRadius_mm * Math.Cos(theta),
                         pitchRadius_mm * Math.Sin(theta));
        }
        return result;
    }

    // ────────────────────── Annular rows ──────────────────────

    private static (double, double)[] PlaceAnnularRows(int n, double outerPitchRadius_mm)
    {
        // Pick ring count so each ring has ≥ MinElementsPerRing elements
        // AND rings are evenly spaced out to the requested pitch radius.
        // Ring k (0-indexed) carries n_k ≈ n × r_k / Σ r_j elements.
        int ringCount = Math.Max(1, (int)Math.Round(Math.Sqrt(n / (double)MinElementsPerRing)));
        ringCount = Math.Max(ringCount, 1);

        // Radii: centre out to outer pitch, avoiding the axis (min 30 %).
        double[] radii = new double[ringCount];
        for (int r = 0; r < ringCount; r++)
        {
            double frac = ringCount == 1 ? 1.0 : 0.35 + 0.65 * r / (ringCount - 1);
            radii[r] = outerPitchRadius_mm * frac;
        }

        // Allocate element counts to rings proportional to radius.
        double totalR = 0.0;
        for (int r = 0; r < ringCount; r++) totalR += radii[r];
        int[] perRing = new int[ringCount];
        int allocated = 0;
        for (int r = 0; r < ringCount - 1; r++)
        {
            perRing[r] = Math.Max(MinElementsPerRing, (int)Math.Round(n * radii[r] / totalR));
            allocated += perRing[r];
        }
        perRing[ringCount - 1] = Math.Max(MinElementsPerRing, n - allocated);

        // Emit positions.
        var list = new List<(double, double)>();
        for (int r = 0; r < ringCount; r++)
        {
            int count = perRing[r];
            double phaseOffset = (r % 2 == 0) ? 0.0 : (Math.PI / count);
            for (int k = 0; k < count; k++)
            {
                double theta = 2.0 * Math.PI * k / count + phaseOffset;
                list.Add((radii[r] * Math.Cos(theta), radii[r] * Math.Sin(theta)));
            }
        }
        return list.ToArray();
    }

    // ────────────────────── Hexagonal ──────────────────────

    private static (double, double)[] PlaceHexagonal(
        int n, double pitchRadius_mm, double chamberRadius_mm, double elementSpacing_mm)
    {
        // Auto-derive element spacing from count and bounding disc area:
        //   area of disc of radius pitchRadius ≈ N * (√3/2) s²
        double autoSpacing = pitchRadius_mm > 0 && n > 0
            ? Math.Sqrt(2.0 * Math.PI * pitchRadius_mm * pitchRadius_mm
                      / (Math.Sqrt(3) * n))
            : 5.0;
        double s = elementSpacing_mm > 0 ? elementSpacing_mm : autoSpacing;
        s = Math.Max(s, 0.5);

        // Triangular lattice: rows offset by s/2 in y; row spacing = s·√3/2.
        double rowSpacing = s * Math.Sqrt(3) * 0.5;
        int maxRadialSteps = (int)Math.Ceiling(pitchRadius_mm / rowSpacing) + 1;

        var candidates = new List<(double y, double z, double r)>();
        double maxR = Math.Min(pitchRadius_mm, chamberRadius_mm * 0.95);

        for (int ry = -maxRadialSteps; ry <= maxRadialSteps; ry++)
        {
            double y = ry * rowSpacing;
            double xShift = (ry % 2 == 0) ? 0.0 : s * 0.5;
            for (int cx = -maxRadialSteps; cx <= maxRadialSteps; cx++)
            {
                double z = cx * s + xShift;
                double rad = Math.Sqrt(y * y + z * z);
                if (rad <= maxR) candidates.Add((y, z, rad));
            }
        }

        // Sort candidates by radius ascending; take the closest N to the axis
        // that fit. Preserves the "inner elements first" heritage look.
        candidates.Sort((a, b) => a.r.CompareTo(b.r));
        int take = Math.Min(n, candidates.Count);
        var result = new (double, double)[take];
        for (int i = 0; i < take; i++) result[i] = (candidates[i].y, candidates[i].z);
        return result;
    }
}
