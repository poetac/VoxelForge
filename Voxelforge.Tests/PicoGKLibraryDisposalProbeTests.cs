// PicoGKLibraryDisposalProbeTests.cs — Pitfall-#8 probe for PicoGK 2.0.0 (Sprint D).
//
// CLAUDE.md pitfall #8 (ADR-005): instantiating `new PicoGK.Library(vox)` inside an
// xUnit test crashes the test host on dispose (GLFW / OpenVDB teardown) under
// PicoGK 1.7.7.5. PicoGK 2.0.0 introduced non-global scoped Library instantiation
// with proper lifetime management. This test verifies whether the disposal crash
// is resolved.
//
// Outcome interpretation:
//   PASS → pitfall #8 RESOLVED under PicoGK 2.0.0. Open a follow-on issue to
//           retire ADR-005 and migrate voxel tests out of subprocess.
//   CRASH (test host abort) → pitfall #8 still present under 2.0.0.
//           The subprocess workaround (SubprocessRunner) remains required.
//
// Uses PicoGK 2.0's library-parameterized overloads directly (no LibraryScope
// ambient) to exercise the exact new API path, distinct from the subprocess tests.

using System.Numerics;
using PicoGK;
using Xunit;

namespace Voxelforge.Tests;

public class PicoGKLibraryDisposalProbeTests
{
    [Fact]
    public void PicoGK_ScopedLibrary_DisposesCleanly_InXUnit()
    {
        // If this crashes the test host on dispose, pitfall #8 is still present.
        using var lib = new Library(0.5f);

        // Use the PicoGK 2.0 library-parameterized factory — avoids global singleton.
        var center = new Vector3(0f, 0f, 0f);
        var voxels = Voxels.voxSphere(lib, center, 10f);

        // Mesh the sphere and count triangles.
        var mesh = voxels.mshAsMesh();

        Assert.True(mesh.nTriangleCount() > 0,
            "Expected at least one triangle from sphere SDF voxelisation via PicoGK 2.0 scoped Library.");
    }
}
