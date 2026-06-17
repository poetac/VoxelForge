// DragDropRouter.cs — Form-level drag-and-drop target resolver.
//
// Determines what to do with a dropped file based on its
// extension. A pure path → Target mapper so tests can cover
// the routing without spinning up a live Form.
//
// Routing:
//   *.rcd.json → DesignJson  (Load Design)
//   *.stl      → InjectorStl (populate injector-STL path + enable)
//   *.csv      → MeasuredData (Load Test Data)
//   (other)    → None        (ignored)
//
// Order matters: `.rcd.json` is checked before the generic `.json`
// (not supported) so the compound extension wins.

using System;

namespace Voxelforge.UI;

public static class DragDropRouter
{
    public enum Target
    {
        None,
        DesignJson,
        InjectorStl,
        MeasuredData,
    }

    public static Target Resolve(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return Target.None;
        string lower = path.Trim().ToLowerInvariant();

        // Compound extension wins over its bare-suffix cousin.
        if (lower.EndsWith(".rcd.json", StringComparison.Ordinal)) return Target.DesignJson;
        if (lower.EndsWith(".stl",      StringComparison.Ordinal)) return Target.InjectorStl;
        if (lower.EndsWith(".csv",      StringComparison.Ordinal)) return Target.MeasuredData;
        return Target.None;
    }
}
