// LibraryScope.cs — Ambient PicoGK Library for nuclear-pillar voxel builds.
// Nuclear pillar copy — parallel to Voxelforge.Marine.Voxels/LibraryScope.cs
// and Voxelforge.ElectricPropulsion.Voxels/LibraryScope.cs.
// See the Voxelforge.Voxels copy for the full rationale (PicoGK 2.0.0
// non-global Library + ThreadStatic ambient pattern).

using PicoGK;

namespace Voxelforge.Nuclear.Geometry;

internal static class LibraryScope
{
    [ThreadStatic] private static Library? _current;

    internal static Library? Current => _current;

    internal sealed class Token : IDisposable
    {
        private readonly Library? _prev;
        internal Token(Library? prev) => _prev = prev;
        public void Dispose() => _current = _prev;
    }

    internal static Token Set(Library lib)
    {
        var tok = new Token(_current);
        _current = lib;
        return tok;
    }

    internal static Voxels MakeVoxels(IImplicit impl, BBox3 bounds) =>
        _current is { } lib
            ? new Voxels(lib, impl, bounds)
            : new Voxels(impl, bounds);
}
