// CpModel.cs — regression-safety toggle for the Sprint C.3 polynomial Cp(T) path.

namespace Voxelforge.Cfd.Config;

/// <summary>
/// Controls whether <see cref="Su2ConfigWriter"/> emits a polynomial Cp(T) γ_eff
/// (Sprint C.3) or the frozen chamber γ (Sprint C.2 fallback).
/// </summary>
public enum CpModel
{
    /// <summary>
    /// Frozen-γ path (Sprint C.2): emit GAMMA_VALUE = GammaChamber; suppress CP_POLYCOEFFS.
    /// Use as a regression-safety switch when the polynomial path is suspect.
    /// </summary>
    FrozenGamma = 0,

    /// <summary>
    /// Polynomial Cp(T) path (Sprint C.3, default): when a non-flat
    /// <see cref="CpPolynomialResult"/> is available, emit GAMMA_VALUE = γ_eff and
    /// CP_POLYCOEFFS. Falls back to FrozenGamma behaviour if the fit is flat.
    /// </summary>
    PolynomialFit = 1,
}
