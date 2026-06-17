// IPropellantTable.cs — Interface every propellant-pair table must expose.
//
// Tables are pure functions of (MR, Pc) → PropellantState. They are
// immutable, thread-safe, and cheap enough to call on every optimiser
// evaluation without caching. The `PropellantTables` facade remains as the
// single public entry point; implementations are internal to Combustion/.

namespace Voxelforge.Combustion;

public interface IPropellantTable
{
    PropellantPair Pair { get; }
    PropellantPairMetadata Metadata { get; }

    /// <summary>
    /// Return a PropellantState at the requested mixture ratio and chamber
    /// pressure. Implementations SHOULD clamp MR to the table's declared
    /// [MR_Min, MR_Max] band; PH-4 implementations also clamp Pc to the
    /// 2-D table envelope (typical [3, 25] MPa).
    /// </summary>
    PropellantState GetState(double mixtureRatio, double chamberPressure_Pa);
}
