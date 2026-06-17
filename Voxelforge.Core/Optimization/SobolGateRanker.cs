using System;
using System.Collections.Generic;
using Voxelforge.Injector;

namespace Voxelforge.Optimization;

/// <summary>
/// Wraps the internal Sobol gate-lever ranking machinery behind a public
/// API surface. Instantiate once per design point, then pass to
/// <see cref="GateExplainer.BuildMarkdown(FeasibilityGateResult, SobolGateRanker?, string)"/>
/// to augment the explainer report with Sobol-ranked lever tables.
/// </summary>
public sealed class SobolGateRanker
{
    private readonly RegenChamberDesign _baseline;
    private readonly OperatingConditions _cond;
    private readonly int _samples;
    // Cached packed baseline and descriptor lookup (built once, reused per Rank call).
    private readonly double[] _packedBaseline;
    private readonly Dictionary<string, SaDesignVariableDescriptor> _descriptorByName;

    public SobolGateRanker(RegenChamberDesign baseline, OperatingConditions cond, int samples = 512)
    {
        _baseline = baseline ?? throw new ArgumentNullException(nameof(baseline));
        _cond     = cond     ?? throw new ArgumentNullException(nameof(cond));
        _samples  = samples > 0 ? samples : throw new ArgumentOutOfRangeException(nameof(samples));

        _packedBaseline = DesignVariableBinder.Pack(baseline, baseline.InjectorElementPattern);

        _descriptorByName = new Dictionary<string, SaDesignVariableDescriptor>(StringComparer.Ordinal);
        foreach (var d in DesignVariableRegistry.For(typeof(RegenChamberDesign)))
            _descriptorByName[d.MemberName] = d;
        foreach (var d in DesignVariableRegistry.For(typeof(InjectorPattern)))
        {
            if (!_descriptorByName.ContainsKey(d.MemberName))
                _descriptorByName[d.MemberName] = d;
        }
    }

    // internal — RankedLever is internal; can't be a public return type on a public method.
    internal RankedLever[] Rank(string constraintId)
    {
        var coupled = GateExplainer.GetCoupledVariables(constraintId);
        if (coupled.Count == 0) return Array.Empty<RankedLever>();

        var descs = new SaDesignVariableDescriptor[coupled.Count];
        for (int i = 0; i < coupled.Count; i++)
        {
            if (!_descriptorByName.TryGetValue(coupled[i], out var d))
                return Array.Empty<RankedLever>();
            descs[i] = d;
        }

        // Model closure: unit-hypercube → perturb the coupled variable slots
        // in the packed baseline → Unpack → PreScreen metric.
        double Model(double[] x)
        {
            var packed = (double[])_packedBaseline.Clone();
            for (int i = 0; i < descs.Length; i++)
            {
                var d = descs[i];
                if (d.Index < packed.Length)
                    packed[d.Index] = d.Min + x[i] * (d.Max - d.Min);
            }
            var design    = DesignVariableBinder.Unpack(packed, _baseline);
            var violation = FeasibilityGate.PreScreen(_cond, design);
            // Return 0.0 when this gate doesn't fire; non-zero ratio when it does.
            if (violation is null || violation.ConstraintId != constraintId)
                return 0.0;
            double limit = violation.Limit;
            return limit > 1e-300 ? violation.ActualValue / limit : 1.0;
        }

        return GateLeverRanker.Rank(constraintId, Model, N: _samples);
    }
}
