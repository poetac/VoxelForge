// RegenChamberForm.RunAllSnapshot.cs — Sprint 6 Track B (2026-04-22):
// Partial-class sibling carrying the nested snapshot type used by the
// "Run All Analyses" toggle (chkRunAllAnalyses).
//
// This is a very small file (the data class is 8 lines) — it lives on
// its own because logically it's a well-contained sub-concern and keeping
// it beside the OnRunAllAnalysesToggled event handler in the main form
// was the only reason it was inlined. The partial-class sibling lets
// us file-scope it without changing its visibility (`private sealed`)
// or affecting OnRunAllAnalysesToggled's ability to construct / consume
// it (shared class identity).

namespace Voxelforge.UI;

public sealed partial class RegenChamberForm
{
    /// <summary>
    /// Snapshot of the four analysis-opt-in fields captured when
    /// `chkRunAllAnalyses` transitions from unchecked → checked. The
    /// prior values are restored on the reverse transition so toggling
    /// the master switch is reversible.
    /// </summary>
    private sealed class RunAllSnapshot
    {
        public bool   Chilldown;
        public bool   StartTrans;
        public double TankUllage_MPa;
        public int    EngineCycle;
    }
}
