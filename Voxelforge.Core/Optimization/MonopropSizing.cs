// OOB-11 (issue #340): monopropellant catalyst-bed sizing.
using Voxelforge.Combustion;
using System;
using System.Collections.Generic;

namespace Voxelforge.Optimization;

public static class MonopropSizing
{
    private const double G0 = 9.80665; // m/s²

    public static MonopropResult Size(MonopropDesign design)
    {
        if (design.Propellant == MonopropellantKind.None)
            throw new ArgumentException("Propellant must not be None.", nameof(design));

        var spec = MonopropTables.SpecFor(design.Propellant);

        // Corrected Isp at actual Pc and ε.
        double isp = MonopropTables.Isp(
            design.Propellant,
            design.ChamberPressure_Pa,
            design.ExpansionRatio);

        // Mass flow from thrust equation: F = mdot × g0 × Isp_vac
        double massFlow = design.Thrust_N / (G0 * isp);

        // Thrust coefficient Cf = F / (Pc × A*)  →  A* = F / (Pc × Cf)
        // Cf ≈ Isp × g0 / C*   where C* = sqrt(R_u·Tc / (gamma · (2/(gamma+1))^...))
        // Simpler route: A* from continuity once mdot and Cf are known.
        // Use C* = Isp_vac × g0 / Cf_vac. Cf_vac = Isp_vac × g0 / C*.
        // For C* use isentropic relation: C* = sqrt(gamma·R·Tc) / Gamma_func
        double R = PropellantTables.R_UNIVERSAL / spec.MolWeight_kgmol; // J/(kg·K)
        double gam = spec.Gamma;
        double gammaFunc = Math.Sqrt(gam * Math.Pow(2.0 / (gam + 1.0), (gam + 1.0) / (gam - 1.0)));
        double cStar = Math.Sqrt(gam * R * spec.Tc_K) / gammaFunc;

        // Throat area: A* = mdot × C* / Pc
        double throatArea_m2 = massFlow * cStar / design.ChamberPressure_Pa;
        double throatRadius_m = Math.Sqrt(throatArea_m2 / Math.PI);
        double throatRadius_mm = throatRadius_m * 1000.0;

        // Catalyst bed area from bed diameter.
        double bedRadius_m = (design.CatalystBedDiameter_mm * 1e-3) / 2.0;
        double bedArea_m2 = Math.PI * bedRadius_m * bedRadius_m;
        double catalystLoading = bedArea_m2 > 0 ? massFlow / bedArea_m2 : double.PositiveInfinity;

        var violations = new List<FeasibilityViolation>();
        MonopropGates.EvaluateAll(
            new MonopropResult(isp, massFlow, throatRadius_mm, catalystLoading,
                IsAcceptable: true, Notes: "", Violations: Array.Empty<FeasibilityViolation>()),
            spec,
            violations);

        bool acceptable = violations.TrueForAll(
            v => v.ConstraintId != "MONOPROP_CATALYST_OVERLOADED");

        string notes = violations.Count == 0
            ? "OK"
            : string.Join("; ", violations.ConvertAll(v => v.ConstraintId));

        return new MonopropResult(
            Isp_vac_s:          isp,
            MassFlow_kgs:       massFlow,
            ThroatRadius_mm:    throatRadius_mm,
            CatalystLoading_kgm2s: catalystLoading,
            IsAcceptable:       acceptable,
            Notes:              notes,
            Violations:         violations.AsReadOnly());
    }
}
