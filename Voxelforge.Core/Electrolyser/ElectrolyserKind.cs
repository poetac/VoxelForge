// ElectrolyserKind.cs — Sprint EL.W1 + EL.W2 + B.2-Alk + B.2-SOEC
// electrolyser sub-classifier.
//
// Wave-1 shipped the PEM (proton-exchange-membrane) electrolyser — the
// commercial workhorse for green-H₂ production. Wave-2 (Sprint B.3,
// 2026-05-13) added AEM (anion-exchange-membrane), the lower-cost
// platinum-group-metal-free alternative. Sprint B.2-Alk (2026-05-14)
// added Alkaline (KOH cell with diaphragm separator) — the mature,
// century-old industrial process. Sprint B.2-SOEC adds high-temperature
// solid-oxide electrolysis (700-850 °C steam electrolysis with
// cogeneration synergy) — a fundamentally different physics path
// (high-T thermo + ionic O²⁻ conduction in YSZ, steam reactant) where
// V_cell typically lands BELOW the 1.481 V HHV thermo-neutral voltage,
// so η_HHV exceeds 1.0 on electric input as the cell absorbs heat to
// complete the endothermic reaction.
//
// PEM cluster (Nel A485 / ITM Power HGas / Cummins HyLYZER class):
//   T ~ 60-80 °C; P ~ 10-30 bar
//   V_cell ~ 1.7-2.0 V at i = 1-2 A/cm²
//   Stack efficiency ~ 60-70 % HHV (more thermodynamics in the cluster
//                                    fit than in PG.W1 because PEM-EL
//                                    needs voltage ABOVE E_Nernst)
//
// AEM cluster (Enapter EL-2.1 / Hydrolite / OxEon Energy class):
//   T ~ 50-70 °C; P ~ 1-35 bar
//   V_cell ~ 1.75-1.95 V at i = 0.5-1.0 A/cm² (lower i than PEM
//                                              because higher R_AS)
//   Stack efficiency ~ 65-80 % HHV (stack-only; system ~ 70 %)
//   Catalyst NiFe-LDH (no platinum-group metals)
//
// Alkaline cluster (Nel A485 / Thyssenkrupp / Asahi-Kasei / Hydrogenics
// HyLYZER class):
//   T ~ 60-90 °C; P ~ 1-30 bar (atmospheric AND pressurised products)
//   V_cell ~ 1.80-2.00 V at i = 0.2-0.4 A/cm² (much lower i than PEM
//                                              because higher Tafel
//                                              slope on Ni catalyst +
//                                              moderate separator R_AS)
//   Stack efficiency ~ 65-80 % HHV
//   Cathode: Ni / Ni-alloy; anode: Ni-Mo or Ni-Co; separator: Zirfon
//   Perl (PPS + ZrO₂) or asbestos legacy
//
//   Defining property vs PEM/AEM: alkaline OER kinetics on Ni-based
//   catalysts run a HIGHER Tafel slope (~ 90 mV/dec at cell level) than
//   IrO₂/NiFe-LDH (~ 60 mV/dec). Compensates with higher exchange
//   current density and runs at lower nominal i to keep V_cell in band.
//
// SOEC cluster (Sunfire HyLink / Topsoe HTSE / Ceres Power class):
//   T ~ 700-850 °C; P ~ 1-5 bar (atmospheric dominant)
//   V_cell ~ 1.10-1.40 V at i = 0.5-1.0 A/cm² — BELOW the 1.481 V HHV
//                                                thermo-neutral voltage
//   Electric-input HHV efficiency > 1.0 at design (cell absorbs heat to
//                                                  drive endothermic
//                                                  reaction)
//   Reactant: steam (H₂O vapour) rather than liquid water
//   Cathode: Ni-YSZ cermet; anode: LSM or LSCF perovskite;
//   Electrolyst: thin (10-20 µm) YSZ anode-supported
//
//   Defining property vs PEM/AEM/Alkaline: the Nernst voltage formula
//   uses a separate steam-electrolysis anchor (~ 0.93 V at 800 °C) and
//   a gentler temperature slope (-0.234 mV/K vs -0.85 mV/K for liquid
//   water); the linear extrapolation used by the liquid-T kinds
//   diverges from the steam-electrolysis cluster above ~ 150 °C.

namespace Voxelforge.Electrolyser;

/// <summary>
/// Sub-classification of electrolyser (Sprint EL.W1 + EL.W2 + B.2-Alk
/// + B.2-SOEC).
/// </summary>
internal enum ElectrolyserKind
{
    /// <summary>Degenerate sentinel — not a valid design choice.</summary>
    None = 0,

    /// <summary>
    /// Proton-exchange-membrane (PEM) electrolyser. Liquid-water in →
    /// H₂ + O₂ out. Operates at 60-80 °C and 10-30 bar; commercial
    /// MW-scale stacks. Wave-1 baseline.
    /// </summary>
    Pem = 1,

    /// <summary>
    /// Anion-exchange-membrane (AEM) electrolyser. Liquid-water in →
    /// H₂ + O₂ out. Operates at 50-70 °C and 1-35 bar; commercial
    /// kW-scale stacks. Wave-2 extension — same architecture as PEM
    /// but with anion-exchange membrane (Sustainion / Aemion /
    /// Piperion) and NiFe-LDH OER catalyst (no platinum-group metals).
    /// Higher R_AS than PEM (≈ 0.30 vs 0.15 Ω·cm²); operating current
    /// density runs lower as a consequence (0.5-1.0 A/cm² vs 1-2 A/cm²
    /// for PEM).
    /// </summary>
    Aem = 2,

    /// <summary>
    /// Alkaline (KOH) electrolyser with diaphragm separator. Liquid-
    /// water + KOH electrolyte in → H₂ + O₂ out. Operates at 60-90 °C
    /// and 1-30 bar (atmospheric AND pressurised variants ship at
    /// MW-scale; Nel A485 / Thyssenkrupp / Asahi-Kasei / Hydrogenics
    /// HyLYZER class). Sprint B.2-Alk extension — same loss-decomposition
    /// shape as PEM/AEM (Nernst + Tafel + ohmic) but with the
    /// higher Tafel slope of Ni-based catalysts (~ 90 mV/dec at cell
    /// level vs ~ 60 mV/dec for IrO₂/NiFe-LDH). Runs at lower current
    /// density (0.2-0.4 A/cm²) than PEM to keep V_cell in band; the
    /// mature commercial workhorse with the lowest CAPEX per kW.
    /// </summary>
    Alkaline = 3,

    /// <summary>
    /// Solid-oxide electrolyser cell (SOEC). High-temperature steam
    /// electrolysis: H₂O(g) → H₂ + ½O₂ over a YSZ-based ceramic
    /// electrolyte at 700-850 °C. Sprint B.2-SOEC extension — a
    /// fundamentally different physics path from PEM/AEM/Alkaline
    /// (high-T thermo + ionic O²⁻ conduction; steam reactant rather
    /// than liquid water). V_cell typically lands at 1.10-1.40 V,
    /// BELOW the 1.481 V HHV thermo-neutral voltage; the cell absorbs
    /// heat from the surroundings to complete the endothermic reaction,
    /// giving η_HHV &gt; 1.0 on electric input (correct and physical —
    /// the SOEC value proposition for waste-heat recovery from
    /// adjacent industrial processes). Commercial class: Sunfire
    /// HyLink, Topsoe HTSE, Ceres Power.
    /// </summary>
    Soec = 4,
}
