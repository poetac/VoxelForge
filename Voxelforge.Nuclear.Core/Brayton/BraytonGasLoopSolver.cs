// BraytonGasLoopSolver.cs — Sprint NU.W3 closed-cycle He Brayton model
// for bimodal NTR.
//
// Stateless, allocation-free, deterministic. Computes electric power
// output, Brayton thermal efficiency, and component-level diagnostics
// (turbine inlet T, alternator-RPM, recuperator effectiveness) for a
// closed-cycle helium Brayton loop coupled to the reactor.
//
// Physics summary (closed Brayton cycle, He working fluid):
//
//   Carnot bound:                η_carnot = 1 − T_cold / T_hot
//   Real efficiency (component): η_real   = η_t · η_c · (1 − T_cold/T_hot) ·
//                                            (1 − f_aux − f_recup_loss)
//
//   where η_t / η_c are turbine + compressor isentropic efficiencies
//   (typical 0.85–0.90 for space-Brayton hardware), and f_aux captures
//   pump + control auxiliary loads (~3–5 %).
//
//   Reactor heat absorbed by Brayton loop:
//     Q_brayton = m_He · cp_He · (T_hot − T_cold_in)
//
//   Net electric output:
//     P_electric = η_real · Q_brayton
//
//   Alternator design RPM is geometry-driven (turbine rotor diameter, blade
//   speed limit); reported here as a derived diagnostic for the gate.
//
// He cp ≈ 5193 J/(kg·K) (Krane, "Modern Physics", table; monatomic γ=5/3
// gives cp = (5/2)·R/M = 2.5·8314/4.003 = 5193).
//
// Reference: NASA SP-100 / SAFE-400 derivative concepts; Brayton loop
// turbine inlet T ≈ 1200–1400 K, He pressure ≈ 5–15 MPa, alternator-
// shaft ≈ 30 000–60 000 RPM.
//
// Validation tolerance per ADR-029 D4 generalised: ±15 % electric power,
// ±10 % thermal efficiency. The cluster is built on classroom-scale
// Brayton fits; real space-Brayton hardware (Capstone µturbine /
// Sunpower Stirling) doesn't exist for space NTR coupling.

using System;

namespace Voxelforge.Nuclear.Brayton;

/// <summary>
/// Output of the closed-cycle Brayton gas-loop solver.
/// </summary>
/// <param name="ElectricPowerOutput_kWe">Net electric power output P_electric [kW_electric].</param>
/// <param name="ThermalEfficiency">η_thermal = P_electric / Q_brayton [-].</param>
/// <param name="CarnotEfficiency">η_carnot = 1 − T_cold/T_hot [-] — upper bound.</param>
/// <param name="ReactorPowerToBrayton_MW">Q_brayton (thermal power tapped from the reactor for the loop) [MW].</param>
/// <param name="HeMassFlow_kgs">He working-fluid mass flow ṁ_He [kg/s].</param>
/// <param name="TurbineInletTemp_K">T_hot at turbine inlet [K].</param>
/// <param name="AlternatorRpm">Alternator design RPM [revolutions/min].</param>
/// <param name="RecuperatorEffectiveness">ε_recup [-].</param>
public sealed record BraytonGasLoopResult(
    double ElectricPowerOutput_kWe,
    double ThermalEfficiency,
    double CarnotEfficiency,
    double ReactorPowerToBrayton_MW,
    double HeMassFlow_kgs,
    double TurbineInletTemp_K,
    double AlternatorRpm,
    double RecuperatorEffectiveness);

/// <summary>
/// Closed-cycle He Brayton gas-loop model for bimodal NTR.
/// </summary>
public static class BraytonGasLoopSolver
{
    /// <summary>He specific heat at constant pressure [J/(kg·K)] — monatomic γ=5/3.</summary>
    public const double HeliumCp_JkgK = 5193.0;

    /// <summary>He molar mass [g/mol].</summary>
    public const double HeliumMolarMass_gmol = 4.003;

    /// <summary>
    /// Turbine isentropic efficiency (space-Brayton cluster anchor 0.88).
    /// </summary>
    public const double DefaultTurbineEfficiency = 0.88;

    /// <summary>
    /// Compressor isentropic efficiency (space-Brayton cluster anchor 0.86).
    /// </summary>
    public const double DefaultCompressorEfficiency = 0.86;

    /// <summary>
    /// Auxiliary load fraction (pumps + control) — 4 % cluster mid-band.
    /// </summary>
    public const double DefaultAuxiliaryLoadFraction = 0.04;

    /// <summary>
    /// Recuperator effectiveness cluster anchor (high-efficiency space
    /// recuperator). Drives the recuperator-loss term — higher ε pushes the
    /// cycle closer to Carnot.
    /// </summary>
    public const double DefaultRecuperatorEffectiveness = 0.90;

    /// <summary>
    /// Cold-side T (post-recuperator + cooler) at the compressor inlet [K].
    /// Closed-loop space-Brayton clusters around 350–450 K (radiator-limited);
    /// 400 K mid-band anchor.
    /// </summary>
    public const double ColdSideTempAnchor_K = 400.0;

    /// <summary>
    /// Solve the He Brayton gas loop.
    /// </summary>
    /// <param name="reactorThermalPower_MW">Reactor total thermal power [MW] (all available; the solver determines how much routes through the Brayton loop).</param>
    /// <param name="electricPowerTarget_kWe">Design electric-power target [kW_e]. The solver sizes the He mass flow to hit this.</param>
    /// <param name="turbineInletTemp_K">T_hot at the turbine inlet [K].</param>
    /// <param name="hePressure_bar">He working-fluid pressure (high-side) [bar].</param>
    /// <param name="alternatorRpm">Design alternator shaft RPM.</param>
    /// <param name="recuperatorEffectiveness">Optional ε_recup override; <see cref="double.NaN"/> uses <see cref="DefaultRecuperatorEffectiveness"/>.</param>
    /// <returns>Solved Brayton state.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when any required numeric argument is NaN or non-positive,
    /// when <paramref name="turbineInletTemp_K"/> does not exceed the
    /// cold-side anchor <see cref="ColdSideTempAnchor_K"/>, or when
    /// <paramref name="recuperatorEffectiveness"/> is finite and outside
    /// [0, 1].
    /// </exception>
    public static BraytonGasLoopResult Solve(
        double reactorThermalPower_MW,
        double electricPowerTarget_kWe,
        double turbineInletTemp_K,
        double hePressure_bar,
        double alternatorRpm,
        double recuperatorEffectiveness = double.NaN)
    {
        if (double.IsNaN(reactorThermalPower_MW) || reactorThermalPower_MW <= 0)
            throw new ArgumentOutOfRangeException(nameof(reactorThermalPower_MW),
                $"ReactorThermalPower_MW {reactorThermalPower_MW:F3} must be > 0 MW_th.");
        if (double.IsNaN(electricPowerTarget_kWe) || electricPowerTarget_kWe <= 0)
            throw new ArgumentOutOfRangeException(nameof(electricPowerTarget_kWe),
                $"ElectricPowerTarget_kWe {electricPowerTarget_kWe:F3} must be > 0 kW_e.");
        if (double.IsNaN(turbineInletTemp_K) || turbineInletTemp_K <= ColdSideTempAnchor_K)
            throw new ArgumentOutOfRangeException(nameof(turbineInletTemp_K),
                $"TurbineInletTemp_K {turbineInletTemp_K:F1} must exceed the cold-side "
              + $"anchor {ColdSideTempAnchor_K:F0} K.");
        if (double.IsNaN(hePressure_bar) || hePressure_bar <= 0)
            throw new ArgumentOutOfRangeException(nameof(hePressure_bar),
                $"HePressure_bar {hePressure_bar:F3} must be > 0 bar.");
        if (double.IsNaN(alternatorRpm) || alternatorRpm <= 0)
            throw new ArgumentOutOfRangeException(nameof(alternatorRpm),
                $"AlternatorRpm {alternatorRpm:F1} must be > 0 RPM.");
        if (!double.IsNaN(recuperatorEffectiveness)
            && (recuperatorEffectiveness < 0 || recuperatorEffectiveness > 1))
            throw new ArgumentOutOfRangeException(nameof(recuperatorEffectiveness),
                $"RecuperatorEffectiveness {recuperatorEffectiveness:F3} must be NaN or in [0, 1].");

        double eps_recup = double.IsNaN(recuperatorEffectiveness)
            ? DefaultRecuperatorEffectiveness
            : recuperatorEffectiveness;

        // 1. Carnot bound.
        double eta_carnot = 1.0 - ColdSideTempAnchor_K / turbineInletTemp_K;

        // 2. Real cycle efficiency — component-efficiency × Carnot bound,
        //    with auxiliary + recuperator-loss subtraction. The recuperator-
        //    loss term is (1 − ε_recup) penalising heat that isn't
        //    recovered.
        double eta_real = DefaultTurbineEfficiency * DefaultCompressorEfficiency
                        * eta_carnot
                        * (1.0 - DefaultAuxiliaryLoadFraction)
                        * (1.0 - 0.5 * (1.0 - eps_recup));

        eta_real = Math.Max(0.01, Math.Min(eta_carnot, eta_real));

        // 3. Reactor heat absorbed by the Brayton loop = P_electric / η.
        double Q_brayton_W  = electricPowerTarget_kWe * 1000.0 / eta_real;
        double Q_brayton_MW = Q_brayton_W * 1e-6;

        // Cap at reactor total power — can't tap more than the reactor produces.
        if (Q_brayton_MW > reactorThermalPower_MW)
        {
            Q_brayton_MW = reactorThermalPower_MW;
            Q_brayton_W  = reactorThermalPower_MW * 1e6;
            electricPowerTarget_kWe = Q_brayton_W * eta_real / 1000.0;
        }

        // 4. He mass flow from the energy balance.
        // He passes through the loop heating from T_cold to T_hot:
        //   Q_brayton = ṁ_He · cp · (T_hot − T_cold)
        double mDot_He = Q_brayton_W / (HeliumCp_JkgK * (turbineInletTemp_K - ColdSideTempAnchor_K));

        return new BraytonGasLoopResult(
            ElectricPowerOutput_kWe:  electricPowerTarget_kWe,
            ThermalEfficiency:        eta_real,
            CarnotEfficiency:         eta_carnot,
            ReactorPowerToBrayton_MW: Q_brayton_MW,
            HeMassFlow_kgs:           mDot_He,
            TurbineInletTemp_K:       turbineInletTemp_K,
            AlternatorRpm:            alternatorRpm,
            RecuperatorEffectiveness: eps_recup);
    }
}
