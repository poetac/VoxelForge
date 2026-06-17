// OOB-11 (issue #340): monopropellant thruster design record + result.
// Standalone from RegenChamberDesign — no schema bump needed.
using Voxelforge.Combustion;
using System.Collections.Generic;

namespace Voxelforge.Optimization;

public sealed record MonopropDesign
{
    public MonopropellantKind Propellant { get; init; } = MonopropellantKind.None;
    public double Thrust_N { get; init; } = 10.0;
    public double ChamberPressure_Pa { get; init; } = 2e6;
    public double ExpansionRatio { get; init; } = 50.0;
    public double CatalystBedDiameter_mm { get; init; } = 20.0;
    public double CatalystBedLength_mm { get; init; } = 40.0;
}

public sealed record MonopropResult(
    double Isp_vac_s,
    double MassFlow_kgs,
    double ThroatRadius_mm,
    double CatalystLoading_kgm2s,
    bool IsAcceptable,
    string Notes,
    IReadOnlyList<FeasibilityViolation> Violations);
