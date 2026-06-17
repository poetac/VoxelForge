// LibraryScope.cs — Ambient PicoGK Library for headless subprocess contexts.
//
// PicoGK 2.0.0 removed automatic global-singleton registration from the
// `new Library(float)` constructor. In interactive mode (Library.Go()) the
// global singleton is still set, so `new Voxels(impl, bounds)` (old API) keeps
// working on that path. In headless subprocess mode the scoped Library is NOT
// registered globally, causing `new Voxels(impl, bounds)` → Library.oLibrary()
// to throw "Your code relies on being called using Library::Go".
//
// Fix: headless entry points (StlExporter, Kiosk) call `LibraryScope.Set(lib)`
// immediately after `new Library(...)`. All builder methods call
// `LibraryScope.MakeVoxels(impl, bounds)` instead of `new Voxels(impl, bounds)`.
// When an ambient library is present the new `Voxels(Library, IImplicit, BBox3)`
// overload is used; otherwise the old overload is used (interactive/Go() path).

using PicoGK;

namespace Voxelforge.Geometry;

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

    /// <summary>
    /// Set <paramref name="lib"/> as the ambient library for this thread.
    /// Dispose the returned token to restore the previous ambient (LIFO).
    /// </summary>
    internal static Token Set(Library lib)
    {
        var tok = new Token(_current);
        _current = lib;
        return tok;
    }

    /// <summary>
    /// Create a <see cref="Voxels"/> from an <see cref="IImplicit"/> and a
    /// bounding box, using the ambient library if one was set via
    /// <see cref="Set"/> (headless path) or falling back to the old global-
    /// singleton overload (interactive / Library.Go() path).
    /// </summary>
    internal static Voxels MakeVoxels(IImplicit impl, BBox3 bounds) =>
        _current is { } lib
            ? new Voxels(lib, impl, bounds)
            : new Voxels(impl, bounds);
}
