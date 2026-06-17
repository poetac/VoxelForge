// FamilyTokens.cs — single source of truth for Voxelforge.Analyzers family detection.
//
// Voxelforge's family-specific analyzers (CrossFamilyImportAnalyzer / VFA001,
// FamilyNamespacePurityAnalyzer / VFA002) need to recognize family-specific
// assembly names like "Voxelforge.{Family}.{Sub}". This file is the canonical
// list of recognized family tokens; consumers must reference FamilyTokens.All
// rather than hard-coding the list.
//
// To add a new pillar:
//   1. Add the family token (the {Family} segment as it appears in the assembly
//      name) to FamilyTokens.All below.
//   2. Add the corresponding entry to family-allocations.md §1.
//   3. Add the <ProjectReference OutputItemType="Analyzer"> wiring to the new
//      pillar's csproj files (Core, Tests, Voxels, StlExporter).
//
// Notes:
//   • "Solar" is retained as a forward-compat placeholder. No Voxelforge.Solar.*
//     pillar exists today; the token in the list is harmless until one ships.
//   • Analyzer projects target netstandard2.0 and cannot reference Voxelforge.Core,
//     so this SSOT lives inside the analyzer assembly rather than in
//     Voxelforge.Core/Engines/EngineFamilies.cs. See ADR-040.

using System;
using System.Collections.Immutable;

namespace Voxelforge.Analyzers
{
    internal static class FamilyTokens
    {
        /// <summary>
        /// Recognised family tokens (the {Family} segment of "Voxelforge.{Family}.{Sub}"
        /// assembly names). Alphabetical for readability; lookup is by string equality.
        /// Backed by <see cref="ImmutableHashSet{T}"/> for O(1) Contains — analyzer
        /// IDE re-runs hit this on every keystroke, per #618.
        /// </summary>
        internal static readonly ImmutableHashSet<string> All =
            ImmutableHashSet.Create(
                StringComparer.Ordinal,
                "Airbreathing",
                "Cfd",
                "ElectricPropulsion",
                "Marine",
                "Nuclear",
                "Solar");
    }
}
