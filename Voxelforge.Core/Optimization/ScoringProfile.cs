namespace Voxelforge.Optimization;

public sealed record ScoringProfile(
    string Name,
    double WallTPenalty,      // quadratic penalty if T > limit
    double WallTAvg,          // linear on (peak - 500K)
    double DPWeight,          // ΔP/Pc
    double MassWeight,        // mass in grams
    double FeatureWeight,     // penalty if min feature < 0.4 mm
    double StructuralWeight,  // penalty if SF < 1.2
    double CoolantTWeight,    // reward coolant ΔT (drives regen capability)
    // SPRINT 1.3: injector ratio penalties (soft). Zero in legacy profiles;
    // new profiles (e.g. Max Injector Uniformity) set a non-zero value to
    // push SA toward velocity/momentum ratios in the classical mixing band.
    double InjectorRatioWeight = 0.0
);
