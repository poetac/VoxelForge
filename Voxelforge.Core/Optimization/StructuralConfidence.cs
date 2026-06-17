namespace Voxelforge.Optimization;

/// <summary>
/// Qualitative structural-confidence grading above and beyond the
/// analytical hoop/thermal VM margin. Demoted when geometric features
/// introduce stress concentrations the thin-wall model does not
/// capture.
/// </summary>
public enum StructuralConfidence
{
    /// <summary>Plain-bore ports, no flanges. VM margin is representative.</summary>
    High = 0,
    /// <summary>Threaded ports or flanges active — add an FEA pass before print.</summary>
    Medium = 1,
    /// <summary>Threaded axial propellant ports pierce the injector flange — highest stress-concentration risk.</summary>
    Low = 2,
}
