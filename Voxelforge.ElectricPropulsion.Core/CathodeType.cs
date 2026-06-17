// CathodeType.cs — Wave-2 HET cathode style.
//
// Drives the Advisory gate `HET_CATHODE_LIFE_LIMIT`. Two families cover
// the flown HET catalog: hollow cathodes (BPT-4000, SPT-100) and
// thoriated-tungsten filament cathodes (research / lab thrusters).

namespace Voxelforge.ElectricPropulsion;

/// <summary>
/// Cathode neutraliser style for a Hall-Effect Thruster.
/// </summary>
public enum CathodeType
{
    /// <summary>
    /// Sentinel for non-HET designs.
    /// </summary>
    None = 0,

    /// <summary>
    /// Hollow cathode (LaB₆ or BaO impregnated). Flight standard;
    /// long-life. Rated current 5–20 A; life-limited above 1.2 × rated I.
    /// </summary>
    HollowCathode = 1,

    /// <summary>
    /// Thoriated-tungsten filament cathode. Lab-thruster only;
    /// short life. Rated current 1–5 A.
    /// </summary>
    FilamentCathode = 2,
}
