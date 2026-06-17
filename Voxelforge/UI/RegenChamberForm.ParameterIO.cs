// RegenChamberForm.ParameterIO.cs — Sprint 6 Track B (2026-04-22):
// Partial-class sibling carrying the (controls ↔ domain records) I/O
// methods extracted from the RegenChamberForm.cs monolith.
//
// Why a partial class:
//   • Read* / Apply* directly read/write ~80 private WinForms controls
//     declared on RegenChamberForm (`nud*`, `chk*`, `cbo*`, `txt*`).
//     Moving them to a separate class would require either exposing
//     those controls as `internal` / public or re-architecting through
//     a ControlsFacade — both are high-churn changes better deferred.
//   • `_suppressParamEvents` and `PushParams()` stay on the main form;
//     the partial sibling accesses them via shared class identity.
//   • No behaviour change — the class metadata is identical before
//     and after this extraction (modulo PDB line positions).
//
// Signatures preserved verbatim:
//   ReadConditions()  → OperatingConditions
//   ReadDesign()      → RegenChamberDesign
//   ApplyDesign(c, d) → void  (clone of c + d back into the controls,
//                              wrapped in _suppressParamEvents guard,
//                              terminates with PushParams())
//
// Call sites in main form (~24 across event handlers, batch runs,
// tolerance sweeps, overlay logic, and the design-persistence layer)
// all keep working bit-identically.

using System;
using System.Windows.Forms;
using Voxelforge.Geometry;
using Voxelforge.HeatTransfer;
using Voxelforge.Injector;
using Voxelforge.Optimization;

namespace Voxelforge.UI;

public sealed partial class RegenChamberForm
{
    // ═══════════════════════════════════════════════════════════════
    //   Parameter I/O: form controls ↔ OperatingConditions + RegenChamberDesign
    // ═══════════════════════════════════════════════════════════════

    // The form has no first-class UI for the injector element pattern
    // (element type, count, pitch, layout). Instead a loaded design
    // round-trips its pattern through this carry field: ApplyDesign
    // captures d.InjectorElementPattern here, ReadDesign restores it.
    // Manual previews with no carried pattern fall back to AutoSeeder
    // in Program.RegenerateForManualMode.
    private InjectorPattern? _carryInjectorPattern;

    public OperatingConditions ReadConditions()
    {
        int pairIdx = Math.Clamp(cboPropellantPair.SelectedIndex, 0,
                                 Combustion.PropellantPairs.All.Length - 1);
        // Return the user's actual selection. If it's an
        // unimplemented pair, GenerateWith will throw UnsupportedPropellantException;
        // the UI pre-gates this by disabling Generate in OnPropellantPairChanged,
        // but if a programmatic path slips through, the exception is the right
        // behaviour rather than a silent swap to LOX/CH4.
        var pair = Combustion.PropellantPairs.All[pairIdx].Id;
        return new()
        {
            Thrust_N = (double)nudThrustN.Value,
            ChamberPressure_Pa = (double)nudPcPsi.Value * 6894.76,
            MixtureRatio = (double)nudMR.Value,
            CoolantInletTemp_K = (double)nudCoolTK.Value,
            CoolantInletPressure_Pa = (double)nudCoolPMPa.Value * 1e6,
            WallMaterialIndex = cboMaterial.SelectedIndex,
            PropellantPair = pair,
            BartzScalingFactor = (double)nudBartzFactor.Value,
            // Feed-stackup opt-in + filter + umbilical bindings:
            TankUllagePressure_Pa = (double)nudTankUllage_MPa.Value * 1e6,
            FilterStandard           = (FeedSystem.FilterStandard)cboFilterStd.SelectedIndex,
            FilterContaminationFraction = (double)nudFilterContamination.Value,
            UmbilicalStandard        = (Geometry.UmbilicalStandard)cboUmbilicalStd.SelectedIndex,
            // Chilldown:
            IncludeChilldownTransient   = chkChilldownEnable.Checked,
            ChilldownInitialJacketTemp_K = (double)nudChilldownInitT.Value,
            ChilldownTwoPhaseHTC_Wm2K   = (double)nudChilldownHTC.Value,
            ChilldownDoneDeltaT_K       = (double)nudChilldownDoneDT.Value,
            ChilldownMaxTime_s          = (double)nudChilldownMaxT.Value,
            // Start transient:
            IncludeStartTransient        = chkStartTransient.Checked,
            StartValveOpenTime_s         = (double)nudValveOpen_ms.Value      * 1e-3,
            StartIgniterDelay_s          = (double)nudIgniterDelay_ms.Value   * 1e-3,
            StartSimulationDuration_s    = (double)nudStartSimDur_ms.Value    * 1e-3,
            StartSimulationTimeStep_s    = (double)nudStartSimDt_ms.Value     * 1e-3,
            StartHardStartFactor         = (double)nudHardStartFactor.Value,
            OxStartValveOpenTime_s       = (double)nudOxValveOpen_ms.Value    * 1e-3,
            FuelStartValveOpenTime_s     = (double)nudFuelValveOpen_ms.Value  * 1e-3,
            // Turbopump cycle:
            EngineCycle              = (FeedSystem.EngineCycle)cboEngineCycle.SelectedIndex,
            PumpInletPressure_Pa     = (double)nudPumpInletP_MPa.Value     * 1e6,
            PumpDischargePressure_Pa = (double)nudPumpDischargeP_MPa.Value * 1e6,
            PumpEfficiency           = (double)nudPumpEff.Value,
        };
    }

    public RegenChamberDesign ReadDesign() => new()
    {
        ContractionRatio = (double)nudContraction.Value,
        ExpansionRatio = (double)nudExpansion.Value,
        CharacteristicLength_m = (double)nudLStar.Value,
        BellEntranceAngle_deg = (double)nudThetaN.Value,
        BellExitAngle_deg = (double)nudThetaE.Value,
        BellLengthFraction = (double)nudBellFrac.Value,
        ChannelCount = (int)nudChannelCount.Value,
        ChannelHeightChamber_mm = (double)nudHChamber.Value,
        ChannelHeightThroat_mm = (double)nudHThroat.Value,
        ChannelHeightExit_mm = (double)nudHExit.Value,
        RibThickness_mm = (double)nudRib.Value,
        GasSideWallThickness_mm = (double)nudWall.Value,
        OuterJacketThickness_mm = (double)nudJacket.Value,
        SmoothingRadius_mm = (double)nudSmoothing.Value,
        IncludeManifolds = chkManifolds.Checked,
        IncludePorts = chkPorts.Checked,
        ManifoldLength_mm = (double)nudManifoldL.Value,
        PortDiameter_mm = (double)nudPortD.Value,
        ChannelManifoldFilletRadius_mm = (double)nudChannelFillet.Value,
        IncludeInjectorFlange = chkInjectorFlange.Checked,
        InjectorFlangeThickness_mm = (double)nudFlangeThk.Value,
        InjectorFlangeOuterRadiusFactor = (double)nudFlangeORFactor.Value,
        PropellantPortDiameter_mm = (double)nudPropPortD.Value,
        IncludeMountingFlange = chkMountFlange.Checked,
        MountingFlangeThickness_mm = (double)nudMountThk.Value,
        MountingFlangeStandard = MountFlangeStdFromUI(),
        CoolantPortStandard = (PortStandard)cboCoolantPortStd.SelectedIndex,
        PropellantPortStandard = (PortStandard)cboPropPortStd.SelectedIndex,
        FilmCooling = new HeatTransfer.FilmCoolingInputs
        {
            Enabled = chkFilmEnable.Checked,
            FuelFractionAsFilm = (double)nudFilmFrac.Value,
            FilmSlotHeight_mm = (double)nudFilmSlotH.Value,
            InjectionX_mm = (double)nudFilmInjX.Value,
            FilmInletTemp_K = (double)nudFilmInletT.Value,
            BurnoutLength_mm = (double)nudFilmBurnL.Value,
            DecayCoefficient = (double)nudFilmDecay.Value,
            ThroatMixingDegradation = (double)nudFilmThroatMix.Value,
        },
        IncludeInjectorSTL = chkInjectorSTL.Checked,
        InjectorSTLPath = txtInjectorSTLPath.Text ?? "",
        InjectorSTLOffsetX_mm = (double)nudInjectorSTLOffsetX.Value,
        InjectorSTLScale = (double)nudInjectorSTLScale.Value,
        InjectorSTLAutoCenter = chkInjectorSTLAutoCenter.Checked,
        InjectorElementPattern = _carryInjectorPattern,
        // Injector internals (#306 / #307 / #308):
        IgniterType                 = (Geometry.IgniterType)cboIgniterType.SelectedIndex,
        IgniterRadialFraction       = (double)nudIgniterRadialFrac.Value,
        FuelDomeDepth_mm            = (double)nudFuelDomeDepth_mm.Value,
        OxDomeDepth_mm              = (double)nudOxDomeDepth_mm.Value,
        DomeInletDiameter_mm        = (double)nudDomeInletDia_mm.Value,
        IncludeAntiVortexBaffle     = chkAntiVortexBaffle.Checked,
        IncludeCoolantCrossover     = chkCoolantCrossover.Checked,
        CoolantCrossoverDiameter_mm = (double)nudCoolantCrossoverDia_mm.Value,
        ProofFactor = (double)nudProofFactor.Value,
        ToleranceSampleCount = (int)nudTolSamples.Value,
        // UI couples wall↔jacket and channel↔rib; users can override the
        // design-record fields individually via saved JSON if independent
        // values are needed.
        WallThicknessTolerance_mm = (double)nudTolWall.Value,
        JacketThicknessTolerance_mm = (double)nudTolWall.Value,
        ChannelHeightTolerance_mm = (double)nudTolChannel.Value,
        RibThicknessTolerance_mm = (double)nudTolChannel.Value,
        // ChannelTopology dropdown.
        ChannelTopology = (Optimization.ChannelTopology)cboTopology.SelectedIndex,
        // Sprint 10 Track A (2026-04-23): preburner-cooling opt-in UI.
        IncludePreburnerRegenCooling = chkPreburnerCooling.Checked,
        PreburnerChannelCount        = (int)nudPreburnerChCount.Value,
        PreburnerChannelWidth_mm     = (double)nudPreburnerChWidth_mm.Value,
        PreburnerChannelDepth_mm     = (double)nudPreburnerChDepth_mm.Value,
        PreburnerWallThickness_mm    = (double)nudPreburnerWallT_mm.Value,
        // Sprint 15 / Track G (2026-04-22): aerospike plug-channel
        // regen-cooling opt-in. Silently inert on non-aerospike
        // topologies — AerospikeOptimization.ToSpec only consumes
        // these when ChannelTopology is Aerospike.
        IncludeAerospikeRegenCooling   = chkAerospikeCooling.Checked,
        AerospikePlugChannelCount      = (int)nudAerospikeChCount.Value,
        AerospikePlugChannelWidth_mm   = (double)nudAerospikeChWidth_mm.Value,
        AerospikePlugChannelDepth_mm   = (double)nudAerospikeChDepth_mm.Value,
        AerospikePlugWallThickness_mm  = (double)nudAerospikeWallT_mm.Value,
        // Sprint fix (2026-04-25): aerospike geometry knobs. SA-tunable
        // dims 22 + 23 in DesignVariableRegistry; previously the UI had
        // no surface for them, so Aerospike-topology designs couldn't
        // override the defaults (PlugLengthRatio=0.30 = 70 % truncation,
        // ContractionRatio=6.0).
        PlugLengthRatio                = (double)nudPlugLengthRatio.Value,
        AerospikeContractionRatio      = (double)nudAerospikeContractionRatio.Value,
        // Sprint 27 (2026-04-23): LPBF printability opt-in. Drives the
        // OVERHANG_ANGLE_EXCEEDED / TRAPPED_POWDER_REGION / DRAIN_PATH_MISSING
        // gates.
        IncludeLpbfPrintabilityAnalysis = chkLpbfPrintability.Checked,
        // Look up the profile by index rather than blind-casting so the
        // mapping stays correct if the enum ever gets re-ordered. The
        // dropdown is populated in the same order as LpbfMaterialProfiles.All.
        LpbfMaterial                    =
            Geometry.LpbfAnalysis.LpbfMaterialProfiles.All[
                Math.Clamp(cboLpbfMaterial.SelectedIndex, 0,
                           Geometry.LpbfAnalysis.LpbfMaterialProfiles.All.Length - 1)
            ].Material,
        LpbfPrintOrientationAxis_deg    = (double)nudLpbfOrientationAxis.Value,
        // Sprint 26 (2026-04-23): linear-aerospike geometry knobs.
        // Consumed by AerospikeOptimization.ToSpec only when
        // ChannelTopology = LinearAerospike.
        LinearAerospikePlugWidth_mm    = (double)nudLinearPlugWidth_mm.Value,
        LinearAerospikeAspectRatio     = (double)nudLinearAspectRatio.Value,
    };

    public void ApplyDesign(OperatingConditions c, RegenChamberDesign d)
    {
        // Sprint 14 / Track I / P19: SuspendLayout around the ~60 control
        // assignments so the form does one layout pass at ResumeLayout
        // instead of one per SetNum/SelectedIndex/Checked write. Pairs
        // with the existing _suppressParamEvents guard which short-circuits
        // ValueChanged event handlers; SuspendLayout handles the layout
        // engine separately. Material UI-responsiveness improvement on
        // design-load (no SA-throughput impact since SA never calls
        // ApplyDesign).
        _suppressParamEvents = true;
        SuspendLayout();
        try
        {
            SetNum(nudThrustN, c.Thrust_N);
            SetNum(nudPcPsi, c.ChamberPressure_Pa / 6894.76);
            SetNum(nudCoolTK, c.CoolantInletTemp_K);
            SetNum(nudCoolPMPa, c.CoolantInletPressure_Pa / 1e6);
            cboMaterial.SelectedIndex = Math.Clamp(c.WallMaterialIndex, 0, WallMaterials.All.Length - 1);
            SetNum(nudBartzFactor, c.BartzScalingFactor);

            // Propellant pair FIRST so MR bounds are in the right band before we set MR.
            int pairIdx = 0;
            for (int i = 0; i < Combustion.PropellantPairs.All.Length; i++)
                if (Combustion.PropellantPairs.All[i].Id == c.PropellantPair) { pairIdx = i; break; }
            cboPropellantPair.SelectedIndex = pairIdx;
            var meta = Combustion.PropellantPairs.All[pairIdx];
            lblPairNote.Text = meta.Note;
            nudMR.Minimum = (decimal)meta.MR_Min;
            nudMR.Maximum = (decimal)meta.MR_Max;
            SetNum(nudMR, c.MixtureRatio);

            SetNum(nudContraction, d.ContractionRatio);
            SetNum(nudExpansion, d.ExpansionRatio);
            SetNum(nudLStar, d.CharacteristicLength_m);
            SetNum(nudThetaN, d.BellEntranceAngle_deg);
            SetNum(nudThetaE, d.BellExitAngle_deg);
            SetNum(nudBellFrac, d.BellLengthFraction);
            SetNum(nudChannelCount, d.ChannelCount);
            SetNum(nudHChamber, d.ChannelHeightChamber_mm);
            SetNum(nudHThroat, d.ChannelHeightThroat_mm);
            SetNum(nudHExit, d.ChannelHeightExit_mm);
            SetNum(nudRib, d.RibThickness_mm);
            SetNum(nudWall, d.GasSideWallThickness_mm);
            SetNum(nudJacket, d.OuterJacketThickness_mm);
            SetNum(nudSmoothing, d.SmoothingRadius_mm);
            chkManifolds.Checked = d.IncludeManifolds;
            chkPorts.Checked = d.IncludePorts;
            SetNum(nudManifoldL, d.ManifoldLength_mm);
            SetNum(nudPortD, d.PortDiameter_mm);
            SetNum(nudChannelFillet, d.ChannelManifoldFilletRadius_mm);
            chkInjectorFlange.Checked = d.IncludeInjectorFlange;
            SetNum(nudFlangeThk, d.InjectorFlangeThickness_mm);
            SetNum(nudFlangeORFactor, d.InjectorFlangeOuterRadiusFactor);
            SetNum(nudPropPortD, d.PropellantPortDiameter_mm);
            chkMountFlange.Checked = d.IncludeMountingFlange;
            SetNum(nudMountThk, d.MountingFlangeThickness_mm);
            // Parent audit §4: preserve preset on load.
            int mIdx = (int)d.MountingFlangeStandard;
            if (mIdx >= 0 && mIdx < cboMountFlangeStd.Items.Count)
                cboMountFlangeStd.SelectedIndex = mIdx;
            cboCoolantPortStd.SelectedIndex = Math.Clamp((int)d.CoolantPortStandard, 0, cboCoolantPortStd.Items.Count - 1);
            cboPropPortStd.SelectedIndex = Math.Clamp((int)d.PropellantPortStandard, 0, cboPropPortStd.Items.Count - 1);

            chkFilmEnable.Checked = d.FilmCooling.Enabled;
            SetNum(nudFilmFrac, d.FilmCooling.FuelFractionAsFilm);
            SetNum(nudFilmSlotH, d.FilmCooling.FilmSlotHeight_mm);
            SetNum(nudFilmInjX, d.FilmCooling.InjectionX_mm);
            SetNum(nudFilmInletT, d.FilmCooling.FilmInletTemp_K);
            SetNum(nudFilmBurnL, d.FilmCooling.BurnoutLength_mm);
            SetNum(nudFilmDecay, d.FilmCooling.DecayCoefficient);
            SetNum(nudFilmThroatMix, d.FilmCooling.ThroatMixingDegradation);

            chkInjectorSTL.Checked = d.IncludeInjectorSTL;
            txtInjectorSTLPath.Text = d.InjectorSTLPath ?? "";
            SetNum(nudInjectorSTLOffsetX, d.InjectorSTLOffsetX_mm);
            SetNum(nudInjectorSTLScale, d.InjectorSTLScale);
            chkInjectorSTLAutoCenter.Checked = d.InjectorSTLAutoCenter;

            // Round-trip the injector element pattern via _carryInjectorPattern
            // since the form has no first-class UI controls for it. ReadDesign
            // restores it on the next read. Null-safe: a loaded design with no
            // pattern clears any previously-carried one.
            _carryInjectorPattern = d.InjectorElementPattern;

            // Injector internals (#306 / #307 / #308):
            cboIgniterType.SelectedIndex = Math.Clamp(
                (int)d.IgniterType, 0, cboIgniterType.Items.Count - 1);
            SetNum(nudIgniterRadialFrac,      d.IgniterRadialFraction);
            SetNum(nudFuelDomeDepth_mm,       d.FuelDomeDepth_mm);
            SetNum(nudOxDomeDepth_mm,         d.OxDomeDepth_mm);
            SetNum(nudDomeInletDia_mm,        d.DomeInletDiameter_mm);
            chkAntiVortexBaffle.Checked      = d.IncludeAntiVortexBaffle;
            chkCoolantCrossover.Checked      = d.IncludeCoolantCrossover;
            SetNum(nudCoolantCrossoverDia_mm, d.CoolantCrossoverDiameter_mm);

            SetNum(nudProofFactor, d.ProofFactor);
            SetNum(nudTolSamples, d.ToleranceSampleCount);
            SetNum(nudTolWall, d.WallThicknessTolerance_mm);
            SetNum(nudTolChannel, d.ChannelHeightTolerance_mm);

            // Surface the feed / chilldown / start-transient / turbopump
            // fields on form load.
            SetNum(nudTankUllage_MPa, c.TankUllagePressure_Pa / 1e6);
            cboFilterStd.SelectedIndex = System.Math.Clamp(
                (int)c.FilterStandard, 0, cboFilterStd.Items.Count - 1);
            SetNum(nudFilterContamination, c.FilterContaminationFraction);
            cboUmbilicalStd.SelectedIndex = System.Math.Clamp(
                (int)c.UmbilicalStandard, 0, cboUmbilicalStd.Items.Count - 1);

            cboTopology.SelectedIndex = System.Math.Clamp(
                (int)d.ChannelTopology, 0, cboTopology.Items.Count - 1);

            chkChilldownEnable.Checked = c.IncludeChilldownTransient;
            SetNum(nudChilldownInitT,  c.ChilldownInitialJacketTemp_K);
            SetNum(nudChilldownHTC,    c.ChilldownTwoPhaseHTC_Wm2K);
            SetNum(nudChilldownDoneDT, c.ChilldownDoneDeltaT_K);
            SetNum(nudChilldownMaxT,   c.ChilldownMaxTime_s);

            chkStartTransient.Checked = c.IncludeStartTransient;
            SetNum(nudValveOpen_ms,     c.StartValveOpenTime_s      * 1e3);
            SetNum(nudIgniterDelay_ms,  c.StartIgniterDelay_s       * 1e3);
            SetNum(nudStartSimDur_ms,   c.StartSimulationDuration_s * 1e3);
            SetNum(nudStartSimDt_ms,    c.StartSimulationTimeStep_s * 1e3);
            SetNum(nudHardStartFactor,  c.StartHardStartFactor);
            SetNum(nudOxValveOpen_ms,   c.OxStartValveOpenTime_s    * 1e3);
            SetNum(nudFuelValveOpen_ms, c.FuelStartValveOpenTime_s  * 1e3);

            cboEngineCycle.SelectedIndex = System.Math.Clamp(
                (int)c.EngineCycle, 0, cboEngineCycle.Items.Count - 1);
            SetNum(nudPumpInletP_MPa,     c.PumpInletPressure_Pa     / 1e6);
            SetNum(nudPumpDischargeP_MPa, c.PumpDischargePressure_Pa / 1e6);
            SetNum(nudPumpEff,            c.PumpEfficiency);

            // Sprint 10 Track A: preburner regen-cooling opt-in + channel geometry
            chkPreburnerCooling.Checked = d.IncludePreburnerRegenCooling;
            SetNum(nudPreburnerChCount,    d.PreburnerChannelCount);
            SetNum(nudPreburnerChWidth_mm, d.PreburnerChannelWidth_mm);
            SetNum(nudPreburnerChDepth_mm, d.PreburnerChannelDepth_mm);
            SetNum(nudPreburnerWallT_mm,   d.PreburnerWallThickness_mm);

            // Sprint 15 / Track G: aerospike plug-channel regen-cooling
            // opt-in + channel geometry. Bind regardless of topology so
            // the controls round-trip through Save/Load even on
            // non-aerospike designs (silently ignored downstream).
            chkAerospikeCooling.Checked    = d.IncludeAerospikeRegenCooling;
            SetNum(nudAerospikeChCount,    d.AerospikePlugChannelCount);
            SetNum(nudAerospikeChWidth_mm, d.AerospikePlugChannelWidth_mm);
            SetNum(nudAerospikeChDepth_mm, d.AerospikePlugChannelDepth_mm);
            SetNum(nudAerospikeWallT_mm,   d.AerospikePlugWallThickness_mm);
            // Sprint fix (2026-04-25): aerospike geometry knobs.
            SetNum(nudPlugLengthRatio,            d.PlugLengthRatio);
            SetNum(nudAerospikeContractionRatio,  d.AerospikeContractionRatio);

            // Sprint 27: LPBF printability opt-in + alloy profile + axis override.
            // Match the ReadDesign path: find the profile by Material
            // rather than cast-by-index so enum re-ordering doesn't
            // silently desync the dropdown.
            chkLpbfPrintability.Checked = d.IncludeLpbfPrintabilityAnalysis;
            int lpbfIdx = 0;
            for (int i = 0; i < Geometry.LpbfAnalysis.LpbfMaterialProfiles.All.Length; i++)
            {
                if (Geometry.LpbfAnalysis.LpbfMaterialProfiles.All[i].Material == d.LpbfMaterial)
                {
                    lpbfIdx = i;
                    break;
                }
            }
            cboLpbfMaterial.SelectedIndex = System.Math.Clamp(
                lpbfIdx, 0, cboLpbfMaterial.Items.Count - 1);
            SetNum(nudLpbfOrientationAxis, d.LpbfPrintOrientationAxis_deg);

            // Sprint 26 (2026-04-23): linear-aerospike geometry knobs.
            // Round-trip regardless of topology so the controls survive
            // Save/Load even when the design is on a non-linear topology.
            SetNum(nudLinearPlugWidth_mm,  d.LinearAerospikePlugWidth_mm);
            SetNum(nudLinearAspectRatio,   d.LinearAerospikeAspectRatio);
        }
        finally
        {
            _suppressParamEvents = false;
            ResumeLayout(performLayout: true);
        }
        // Refresh conditional-visibility state once the bulk control
        // update has fully committed. The inline SelectedIndexChanged
        // wiring fires for each intermediate write but the
        // _suppressParamEvents guard inside RecomputeFieldVisibility
        // makes those no-ops; this final call reconciles against the
        // fully-written design.
        RecomputeFieldVisibility();
        PushParams();
    }
}
