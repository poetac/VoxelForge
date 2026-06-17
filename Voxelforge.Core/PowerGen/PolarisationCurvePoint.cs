// PolarisationCurvePoint.cs — Sprint PG.W2 V/I curve sample.
//
// One sample point on the V_cell vs i polarisation curve of a PEM fuel
// cell stack. The curve is the standard fuel-cell-engineering
// characterisation artefact — it shows the operating envelope from
// open-circuit voltage (i = 0) through the operational sweet spot to
// the mass-transport limit (i → i_L).

namespace Voxelforge.PowerGen;

/// <summary>
/// One sample on the V_cell vs i polarisation curve (Sprint PG.W2).
/// </summary>
/// <param name="CurrentDensity_A_cm2">Operating current density i [A/cm²].</param>
/// <param name="CellVoltage_V">Resolved single-cell voltage V_cell [V].</param>
/// <param name="StackElectricPower_W">P_elec at the snapshot [W].</param>
/// <param name="PowerDensity_W_cm2">P_cell / A_active [W/cm²] — figure of merit for stack sizing.</param>
internal sealed record PolarisationCurvePoint(
    double CurrentDensity_A_cm2,
    double CellVoltage_V,
    double StackElectricPower_W,
    double PowerDensity_W_cm2);
