// Su2MeshWriter.cs — Structured 2-D axisymmetric SU2 native mesh (.su2) writer.
//
// Converts a ChamberContour axisymmetric profile to a body-fitted structured
// quadrilateral mesh in the x-r plane (x=axial, r=radial). Geometric stretching
// is applied in the radial direction so the first cell height at the wall is
// h₁=5 μm, satisfying the y⁺≈1 requirement for SST wall-resolved RANS.
//
// Four named boundary markers: inlet (x_min face), outlet (x_max face),
// wall (nozzle contour), axis (symmetry line at r=0).
//
// Mesh density presets (per cfd-validation-spec.md):
//   Coarse   50×20   — CI smoke tests
//   Standard 200×80  — calibration reports
//   Fine     400×160 — publication

using System.Globalization;
using System.Text;
using Voxelforge.Chamber;

namespace Voxelforge.Cfd.Mesh;

/// <summary>Structured 2-D axisymmetric SU2 mesh density presets.</summary>
public enum Su2MeshDensity
{
    /// <summary>50×20 cells — suitable for CI smoke tests (~seconds).</summary>
    Coarse,
    /// <summary>200×80 cells — calibration report quality (~minutes).</summary>
    Standard,
    /// <summary>400×160 cells — publication quality (~10-30 min).</summary>
    Fine,
}

/// <summary>
/// Writes a structured 2-D axisymmetric SU2 native mesh from a <see cref="ChamberContour"/>.
/// </summary>
public static class Su2MeshWriter
{
    // First cell height at the wall (metres): h₁=5 μm gives y⁺≈1 at typical LRE conditions.
    private const double FirstCellHeight_m = 5e-6;

    /// <summary>
    /// Returns the (Nx, Nr) axial × radial cell counts for the given <paramref name="density"/>.
    /// </summary>
    public static (int Nx, int Nr) GridDimensions(Su2MeshDensity density)
        => density switch
        {
            Su2MeshDensity.Coarse   => (50,  20),
            Su2MeshDensity.Standard => (200, 80),
            _                       => (400, 160),
        };

    /// <summary>
    /// Writes a body-fitted structured quad mesh to <paramref name="outputPath"/>.
    /// </summary>
    /// <param name="outputPath">Destination .su2 file path.</param>
    /// <param name="contour">Source axisymmetric nozzle profile.</param>
    /// <param name="density">Mesh resolution preset (default Coarse).</param>
    /// <exception cref="ArgumentException">
    /// Thrown when the contour has fewer than 2 stations or any wall radius is non-positive.
    /// </exception>
    public static void Write(
        string outputPath,
        ChamberContour contour,
        Su2MeshDensity density = Su2MeshDensity.Coarse)
    {
        ArgumentNullException.ThrowIfNull(outputPath);
        ArgumentNullException.ThrowIfNull(contour);

        if (contour.Stations.Length < 2)
            throw new ArgumentException("Contour must have at least 2 stations.", nameof(contour));

        var (nx, nr) = GridDimensions(density);
        double xMinM = contour.Stations[0].X_mm / 1000.0;
        double xMaxM = contour.Stations[^1].X_mm / 1000.0;

        // Step 1: sample Nx+1 axial positions and corresponding wall radii (metres)
        double[] xPos  = new double[nx + 1];
        double[] rWall = new double[nx + 1];

        for (int i = 0; i <= nx; i++)
        {
            xPos[i] = xMinM + (double)i / nx * (xMaxM - xMinM);
            int st = contour.StationAt(xPos[i] * 1000.0);
            rWall[i] = contour.Stations[st].R_mm / 1000.0;

            if (rWall[i] <= 0.0)
                throw new ArgumentException(
                    $"Contour has non-positive wall radius ({rWall[i] * 1000:F3} mm) at x={xPos[i] * 1000:F2} mm.",
                    nameof(contour));
        }

        // Step 2: per-column geometric stretching ratio r, such that
        //   h₁*(r^Nr - 1)/(r - 1) = R_wall  (Newton iteration)
        double[] geoR = new double[nx + 1];
        for (int i = 0; i <= nx; i++)
            geoR[i] = ComputeGeometricRatio(rWall[i], nr);

        // Step 3: build (x, y) coordinates.
        //   node_idx(i, j) = i*(Nr+1) + j   j=0 → axis, j=Nr → wall
        int totalNodes = (nx + 1) * (nr + 1);
        double[] nodeX = new double[totalNodes];
        double[] nodeY = new double[totalNodes];

        // Scratch buffer for per-column geometric progression of r^k (k = 0..Nr-1).
        // Reused across columns; rebuilt only when r ≠ 1.
        double[] powCache = new double[nr];

        for (int i = 0; i <= nx; i++)
        {
            double r  = geoR[i];
            double h1 = FirstCellHeight_m;
            double y  = 0.0;

            nodeX[i * (nr + 1)] = xPos[i];
            nodeY[i * (nr + 1)] = 0.0;

            bool uniform = Math.Abs(r - 1.0) < 1e-12;
            if (!uniform)
            {
                powCache[0] = 1.0;
                for (int k = 1; k < nr; k++) powCache[k] = powCache[k - 1] * r;
            }

            for (int j = 1; j <= nr; j++)
            {
                // Step from axis to wall; step size is largest near axis, smallest at wall.
                // Δy at radial level j = h₁ * r^(Nr-j)   (r^(Nr-1) at j=1, r^0=h₁ at j=Nr)
                double delta = uniform
                    ? rWall[i] / nr
                    : h1 * powCache[nr - j];

                y += delta;
                nodeX[i * (nr + 1) + j] = xPos[i];
                nodeY[i * (nr + 1) + j] = y;
            }

            // Clamp the wall node to the exact contour radius to avoid float drift.
            nodeY[i * (nr + 1) + nr] = rWall[i];
        }

        // Step 4 & 5: write SU2 v8 native format (ASCII)
        using var sw = new StreamWriter(outputPath, append: false, encoding: Encoding.ASCII);
        var ci = CultureInfo.InvariantCulture;

        sw.WriteLine("%");
        sw.WriteLine("% Voxelforge Sprint C.1 — 2-D axisymmetric structured nozzle mesh");
        sw.WriteLine("%");

        // ── Dimension ──────────────────────────────────────────────────────────
        sw.WriteLine("NDIME= 2");

        // ── Interior element connectivity (VTK type 9 = quad) ─────────────────
        sw.WriteLine("%");
        sw.WriteLine("% Inner element connectivity");
        sw.WriteLine("%");
        sw.WriteLine($"NELEM= {nx * nr}");

        int elemIdx = 0;
        for (int i = 0; i < nx; i++)
        {
            for (int j = 0; j < nr; j++)
            {
                int n0 = i       * (nr + 1) + j;
                int n1 = (i + 1) * (nr + 1) + j;
                int n2 = (i + 1) * (nr + 1) + j + 1;
                int n3 = i       * (nr + 1) + j + 1;
                sw.WriteLine($"9\t{n0}\t{n1}\t{n2}\t{n3}\t{elemIdx++}");
            }
        }

        // ── Node coordinates ───────────────────────────────────────────────────
        sw.WriteLine("%");
        sw.WriteLine("% Node coordinates");
        sw.WriteLine("%");
        sw.WriteLine($"NPOIN= {totalNodes}");
        for (int idx = 0; idx < totalNodes; idx++)
            sw.WriteLine($"{nodeX[idx].ToString("G17", ci)}\t{nodeY[idx].ToString("G17", ci)}");

        // ── Boundary markers ───────────────────────────────────────────────────
        sw.WriteLine("%");
        sw.WriteLine("% Boundary elements");
        sw.WriteLine("%");
        sw.WriteLine("NMARK= 4");

        // inlet: i=0 face, j=0..Nr-1 segments
        WriteBoundary(sw, "inlet", nr, j =>
            $"3\t{j}\t{j + 1}");

        // outlet: i=Nx face, j=0..Nr-1 segments
        int baseOut = nx * (nr + 1);
        WriteBoundary(sw, "outlet", nr, j =>
            $"3\t{baseOut + j}\t{baseOut + j + 1}");

        // wall: j=Nr edge, i=0..Nx-1 segments
        WriteBoundary(sw, "wall", nx, i =>
            $"3\t{i * (nr + 1) + nr}\t{(i + 1) * (nr + 1) + nr}");

        // axis: j=0 edge, i=0..Nx-1 segments
        WriteBoundary(sw, "axis", nx, i =>
            $"3\t{i * (nr + 1)}\t{(i + 1) * (nr + 1)}");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static void WriteBoundary(StreamWriter sw, string tag, int count, Func<int, string> lineFor)
    {
        sw.WriteLine($"MARKER_TAG= {tag}");
        sw.WriteLine($"MARKER_ELEMS= {count}");
        for (int k = 0; k < count; k++)
            sw.WriteLine(lineFor(k));
    }

    /// <summary>
    /// Solves h₁*(r^Nr−1)/(r−1) = rWall for the geometric stretching ratio r via Newton
    /// iteration. Returns 1.0 (uniform spacing) when rWall &lt; h₁*Nr.
    /// </summary>
    private static double ComputeGeometricRatio(double rWall, int nr)
    {
        double h1 = FirstCellHeight_m;

        if (rWall < h1 * nr)
            return 1.0; // uniform spacing fallback

        // Initial guess: r ≈ (R_wall / (h1 * Nr))^(1/(Nr-1))
        double r = Math.Max(1.01, Math.Pow(rWall / (h1 * nr), 1.0 / (nr - 1)));

        for (int iter = 0; iter < 30; iter++)
        {
            double rn   = Math.Pow(r, nr);
            double rm1  = r - 1.0;
            double fVal = h1 * (rn - 1.0) / rm1 - rWall;
            double fpVal = h1 * (nr * Math.Pow(r, nr - 1) * rm1 - (rn - 1.0)) / (rm1 * rm1);

            if (Math.Abs(fpVal) < 1e-30) break;

            double rNext = r - fVal / fpVal;

            // Clamp to physical range
            if (rNext < 1.0001) rNext = 1.0001;

            if (Math.Abs(rNext - r) < 1e-10) { r = rNext; break; }
            r = rNext;
        }

        return r;
    }
}
