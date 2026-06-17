// InjectorElementFactory.cs — String-keyed factory for element types.
//
// Allows InjectorPattern to serialise/deserialise the element choice as a
// plain string while keeping the interface-typed reference in memory.
//
// All five element types are fully implemented with working sizing
// models — Pintle (Heister / Dressler), Showerhead (Sutton 9e §9.4),
// Swirl (Bazarov / Abramovich).

namespace Voxelforge.Injector.Elements;

public static class InjectorElementFactory
{
    /// <summary>
    /// All known element types. Every entry is backed by a fully
    /// implemented sizing model.
    /// </summary>
    public static readonly string[] AllTypes =
    {
        "Coax",
        "ImpingingDoublet",
        "Pintle",
        "Showerhead",
        "Swirl",
    };

    /// <summary>
    /// Create an element instance by string key.
    /// Returns a <see cref="CoaxElement"/> for unknown keys (safe fallback).
    /// </summary>
    public static IInjectorElement Create(string elementType) => elementType switch
    {
        "Coax"              => new CoaxElement(),
        "ImpingingDoublet"  => new ImpingingDoubletElement(),
        "Pintle"            => new PintleElement(),
        "Showerhead"        => new ShowerheadElement(),
        "Swirl"             => new SwirlElement(),
        _                   => new CoaxElement(),
    };
}
