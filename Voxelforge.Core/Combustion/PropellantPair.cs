// PropellantPair.cs — Enumeration + registry for supported oxidiser/fuel
// combinations and the tables that back them.
//
// The design here is deliberately open-ended so that a future contributor
// can drop in a new pair with a single new file under Combustion/Tables/*.cs
// and one registry entry. The rest of the codebase (thermal solver,
// structural solver, optimiser, UI) consumes only the common
// `PropellantState` record and never needs to know which pair produced it.
//
// Propellant-dependent *coolant* properties (density, μ, k, Cp vs T, P) are a
// separate concern; at present only methane is implemented in full
// (`Coolant/CoolantProperties.cs`). A future coolant abstraction will parallel
// this one.

namespace Voxelforge.Combustion;

public enum PropellantPair
{
    LOX_CH4 = 0,     // LOX / liquid methane — primary, fully tabulated
    LOX_H2,          // LOX / liquid hydrogen — high-Isp upper stage
    LOX_RP1,         // LOX / RP-1 (kerosene)
    N2O4_MMH,        // Storable hypergolic — spacecraft thrusters
    H2O2_RP1,        // Hydrogen peroxide / RP-1 — "green" bipropellant
}

/// <summary>
/// Static metadata about a propellant pair — what range of mixture ratios
/// is meaningful, what fuel is used for regen cooling, typical pressures,
/// and a one-line note. Used by UI (for defaults and dropdowns) and by
/// the solver (for sanity-clamp warnings).
/// </summary>
public sealed record PropellantPairMetadata(
    PropellantPair Id,
    string Name,
    string FuelSymbol,                  // "CH4", "H2", "RP-1", "MMH"
    string OxidiserSymbol,              // "LOX", "N2O4", "H2O2"
    double MR_Min,                      // usable MR band
    double MR_Max,
    double MR_Default,
    double MR_AtPeakCStar,              // where C* peaks (performance sweet spot)
    bool CoolantIsFuel,                 // regen jacket uses FUEL (true for all LOX-*)
    string CoolantFluidKey,             // "CH4", "H2", "RP-1" — maps to future coolant modules
    bool Implemented,                   // is the table populated with real data?
    string Note);

public static class PropellantPairs
{
    public static readonly PropellantPairMetadata[] All = new[]
    {
        new PropellantPairMetadata(
            Id: PropellantPair.LOX_CH4,
            Name: "LOX / CH\u2084 (methane)",
            FuelSymbol: "CH4", OxidiserSymbol: "LOX",
            MR_Min: 2.0, MR_Max: 5.0, MR_Default: 3.3, MR_AtPeakCStar: 3.2,
            CoolantIsFuel: true, CoolantFluidKey: "CH4",
            Implemented: true,
            Note: "Primary pair. CuCrZr or GRCop-42 jacket. Pc 3\u201320 MPa."),

        new PropellantPairMetadata(
            Id: PropellantPair.LOX_H2,
            Name: "LOX / H\u2082 (hydrogen)",
            FuelSymbol: "H2", OxidiserSymbol: "LOX",
            MR_Min: 3.0, MR_Max: 7.0, MR_Default: 4.0, MR_AtPeakCStar: 4.0,
            CoolantIsFuel: true, CoolantFluidKey: "H2",
            Implemented: true,
            Note: "Highest Isp. Very low MW \u21d2 high C*. Hydrogen embrittlement is a concern."),

        new PropellantPairMetadata(
            Id: PropellantPair.LOX_RP1,
            Name: "LOX / RP-1 (kerosene)",
            FuelSymbol: "RP-1", OxidiserSymbol: "LOX",
            MR_Min: 2.0, MR_Max: 2.8, MR_Default: 2.56, MR_AtPeakCStar: 2.5,
            CoolantIsFuel: true, CoolantFluidKey: "RP-1",
            Implemented: true,
            Note: "Kerosene coking limit caps coolant outlet T \u2a85 600\u202fK."),

        new PropellantPairMetadata(
            Id: PropellantPair.N2O4_MMH,
            Name: "N\u2082O\u2084 / MMH (hypergolic)",
            FuelSymbol: "MMH", OxidiserSymbol: "N2O4",
            MR_Min: 1.5, MR_Max: 2.2, MR_Default: 1.85, MR_AtPeakCStar: 1.85,
            CoolantIsFuel: false, CoolantFluidKey: "MMH",
            Implemented: false,
            Note: "Storable. Hypergolic ignition. Regen cooling unusual \u2014 usually film/radiation cooled."),

        new PropellantPairMetadata(
            Id: PropellantPair.H2O2_RP1,
            Name: "H\u2082O\u2082 / RP-1",
            FuelSymbol: "RP-1", OxidiserSymbol: "H2O2",
            MR_Min: 6.0, MR_Max: 8.5, MR_Default: 7.0, MR_AtPeakCStar: 7.0,
            CoolantIsFuel: true, CoolantFluidKey: "RP-1",
            Implemented: false,
            Note: "\u201cGreen\u201d. 90\u201398\u202f% peroxide. Lower performance than LOX/RP-1."),
    };

    public static PropellantPairMetadata GetMeta(PropellantPair id)
    {
        foreach (var m in All) if (m.Id == id) return m;
        throw new ArgumentOutOfRangeException(nameof(id));
    }

    /// <summary>
    /// Convenience guard for callers that want to skip / gate work
    /// without having to look the metadata entry up themselves. Returns
    /// the same boolean as
    /// <see cref="PropellantPairMetadata.Implemented"/>.
    /// </summary>
    public static bool IsImplemented(PropellantPair id) => GetMeta(id).Implemented;

    /// <summary>
    /// Return the lookup table for the given propellant pair. Throws for
    /// un-implemented pairs — UI should gate on
    /// <see cref="PropellantPairMetadata.Implemented"/> (or call
    /// <see cref="IsImplemented"/>). The thrown exception is
    /// <see cref="PropellantNotImplementedException"/> so callers can
    /// catch it specifically without pattern-matching on the more
    /// generic <see cref="NotImplementedException"/>.
    /// </summary>
    public static IPropellantTable GetTable(PropellantPair id) => id switch
    {
        PropellantPair.LOX_CH4 => LoxMethaneTable.Instance,
        PropellantPair.LOX_H2  => LoxHydrogenTable.Instance,
        PropellantPair.LOX_RP1 => LoxRP1Table.Instance,
        _ => throw new PropellantNotImplementedException(id),
    };
}

/// <summary>
/// Raised by <see cref="PropellantPairs.GetTable"/> when a caller asks
/// for a propellant pair whose CEA table has not been populated yet.
/// Carries the offending pair ID plus its metadata note so UI / CLI
/// callers can give the user a specific actionable message (rather
/// than the previous bare <see cref="NotImplementedException"/>).
/// </summary>
public sealed class PropellantNotImplementedException : NotImplementedException
{
    public PropellantNotImplementedException(PropellantPair pair)
        : base(BuildMessage(pair))
    {
        Pair = pair;
    }

    public PropellantPair Pair { get; }

    private static string BuildMessage(PropellantPair id)
    {
        var meta = PropellantPairs.GetMeta(id);
        return $"Propellant pair {meta.Name} ({id}) is declared but no CEA table "
             + $"is populated yet. Pick an implemented pair (LOX/CH4, LOX/H2, "
             + $"LOX/RP-1) or contribute a table under Combustion/. "
             + $"Note: {meta.Note}";
    }
}
