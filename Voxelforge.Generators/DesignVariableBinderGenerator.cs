// DesignVariableBinderGenerator.cs — Issue #159 (T1.4) source generator.
//
// Walks every property/field tagged with [SaDesignVariable] in the
// consuming compilation unit and emits a partial class
// DesignVariableBinder.Generated containing a pre-built accessor
// table. The runtime DesignVariableBinder consults this table first
// in its hot path; if a property isn't in the generated table (older
// build artifact, runtime-injected attribute, etc.) it falls back to
// the existing Expression.Compile path. This is the "fast lane plus
// safety net" pattern — eliminates ~5-10 ms of compile-delegate
// warmup per process startup AND makes ADR-010's contract compile-
// time-checked (a typo'd attribute on a property is a generator
// emission failure, not a runtime KeyNotFoundException).
//
// Why IIncrementalGenerator: it's the modern Roslyn API (since 4.0).
// Old-style ISourceGenerator runs the whole pipeline on every keystroke
// in the IDE; IIncrementalGenerator caches the syntax-walk results and
// only re-emits when the cached input shape changes.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Voxelforge.Generators
{
    /// <summary>
    /// Emits a partial class
    /// <c>Voxelforge.Optimization.DesignVariableBinder.Generated.cs</c>
    /// with a pre-built accessor table for every <c>[SaDesignVariable]</c>-
    /// tagged member visible to the compilation. The runtime binder
    /// uses this table as a fast-path lookup before falling back to
    /// reflection-built Expression-tree delegates.
    /// </summary>
    [Generator]
    public sealed class DesignVariableBinderGenerator : IIncrementalGenerator
    {
        private const string AttributeFullName =
            "Voxelforge.Optimization.SaDesignVariableAttribute";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Find every property/field with [SaDesignVariable] attribute.
            var taggedMembers = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    fullyQualifiedMetadataName: AttributeFullName,
                    predicate: static (node, _) =>
                        node is PropertyDeclarationSyntax or FieldDeclarationSyntax,
                    transform: static (ctx, _) => ExtractMember(ctx))
                .Where(static info => info is not null)
                .Select(static (info, _) => info!.Value)
                .Collect();

            context.RegisterSourceOutput(taggedMembers, EmitBinderPartial);
        }

        /// <summary>
        /// Symbol-level metadata for one tagged member, captured during
        /// the syntax walk. Implements <see cref="IEquatable{T}"/> so
        /// the incremental cache can short-circuit equal results.
        /// (Plain struct rather than `record struct` because the
        /// netstandard2.0 target doesn't have
        /// <c>System.Runtime.CompilerServices.IsExternalInit</c>.)
        /// </summary>
        private readonly struct MemberInfo : IEquatable<MemberInfo>
        {
            public readonly string DeclaringTypeFullName;
            public readonly string MemberName;
            public readonly string PropertyTypeFullName;
            // True when the member exposes a writable accessor (a property
            // `set`/`init`, or a non-readonly/non-const field). When false the
            // member is get-only/computed and has NO `set_` method — emitting
            // an [UnsafeAccessor(Name="set_X")] extern for it would reference a
            // non-existent accessor that resolves to a MissingMethodException
            // only when first invoked. We instead emit a throwing setter so the
            // failure is a clear, eager NotSupportedException at the binder
            // boundary rather than an obscure JIT-time miss.
            public readonly bool HasSetter;

            public MemberInfo(string declaringTypeFullName, string memberName, string propertyTypeFullName, bool hasSetter)
            {
                DeclaringTypeFullName = declaringTypeFullName;
                MemberName = memberName;
                PropertyTypeFullName = propertyTypeFullName;
                HasSetter = hasSetter;
            }

            public bool Equals(MemberInfo other) =>
                DeclaringTypeFullName == other.DeclaringTypeFullName &&
                MemberName == other.MemberName &&
                PropertyTypeFullName == other.PropertyTypeFullName &&
                HasSetter == other.HasSetter;

            public override bool Equals(object? obj) => obj is MemberInfo other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = DeclaringTypeFullName?.GetHashCode() ?? 0;
                    h = (h * 397) ^ (MemberName?.GetHashCode() ?? 0);
                    h = (h * 397) ^ (PropertyTypeFullName?.GetHashCode() ?? 0);
                    h = (h * 397) ^ HasSetter.GetHashCode();
                    return h;
                }
            }
        }

        private static MemberInfo? ExtractMember(GeneratorAttributeSyntaxContext ctx)
        {
            // ctx.TargetSymbol is the property/field symbol;
            // ctx.TargetNode is the declaration syntax.
            var symbol = ctx.TargetSymbol;
            if (symbol.ContainingType is not { } declaringType)
                return null;

            string declaringFqn = declaringType.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat);
            string memberName = symbol.Name;

            string propertyTypeFqn = symbol switch
            {
                IPropertySymbol p => p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                IFieldSymbol    f => f.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                _                 => string.Empty,
            };

            if (string.IsNullOrEmpty(propertyTypeFqn))
                return null;

            // A property is settable when it has any set/init accessor
            // (init-only properties surface a SetMethod with IsInitOnly=true —
            // still a real `set_` method the [UnsafeAccessor] extern can bind).
            // A field has no `set_` method at all, so the Method-kind extern
            // this generator emits can never bind one; treat fields as
            // unsettable here (none currently carry [SaDesignVariable]) so they
            // route to the throwing setter rather than a broken extern. A
            // Field-kind UnsafeAccessor would be the future fix if a field
            // carrier ever lands.
            bool hasSetter = symbol switch
            {
                IPropertySymbol p => p.SetMethod is not null,
                _                 => false,
            };

            return new MemberInfo(
                declaringTypeFullName: declaringFqn,
                memberName: memberName,
                propertyTypeFullName: propertyTypeFqn,
                hasSetter: hasSetter);
        }

        private static void EmitBinderPartial(
            SourceProductionContext spc,
            ImmutableArray<MemberInfo> members)
        {
            // Group by declaring type so the emitted code keeps a
            // deterministic ordering: types sorted by FQN, members
            // sorted by name within each type.
            var byType = members
                .GroupBy(m => m.DeclaringTypeFullName)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated />");
            sb.AppendLine("// Source-generated by Voxelforge.Generators");
            sb.AppendLine("// (DesignVariableBinderGenerator). DO NOT EDIT BY HAND.");
            sb.AppendLine("//");
            sb.AppendLine("// Issue #159 (T1.4) — compile-time accessor table for every");
            sb.AppendLine("// [SaDesignVariable]-tagged property. Consumed by the partial");
            sb.AppendLine("// DesignVariableBinder in DesignVariableBinder.cs as a fast-path");
            sb.AppendLine("// lookup before its Expression.Compile fallback.");
            sb.AppendLine("//");
            sb.AppendLine("// Setters use [UnsafeAccessor] (.NET 8+) to bypass C# 9's init-only");
            sb.AppendLine("// compile-time restriction without going through PropertyInfo.SetValue");
            sb.AppendLine("// or System.Linq.Expressions. The result is direct callvirt against");
            sb.AppendLine("// the property's set_ method on the underlying type — same IL as a");
            sb.AppendLine("// hand-written non-init setter, but available for init-only too. This");
            sb.AppendLine("// is the AOT-clean path: no Reflection.Emit, no Expression.Compile.");
            sb.AppendLine();
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine("namespace Voxelforge.Optimization");
            sb.AppendLine("{");
            sb.AppendLine("    public static partial class DesignVariableBinder");
            sb.AppendLine("    {");
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Generated accessor entry: keyed by");
            sb.AppendLine("        /// <c>(declaringTypeFqn, memberName)</c>, value is a");
            sb.AppendLine("        /// (boxed-getter, boxed-setter) pair binding directly to");
            sb.AppendLine("        /// the property without going through reflection.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        internal sealed class GeneratedAccessor");
            sb.AppendLine("        {");
            sb.AppendLine("            public global::System.Func<object, object?> Getter { get; }");
            sb.AppendLine("            public global::System.Action<object, object> Setter { get; }");
            sb.AppendLine("            public global::System.Type PropertyType { get; }");
            sb.AppendLine("            public GeneratedAccessor(");
            sb.AppendLine("                global::System.Func<object, object?> getter,");
            sb.AppendLine("                global::System.Action<object, object> setter,");
            sb.AppendLine("                global::System.Type propertyType)");
            sb.AppendLine("            {");
            sb.AppendLine("                Getter = getter;");
            sb.AppendLine("                Setter = setter;");
            sb.AppendLine("                PropertyType = propertyType;");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        // [UnsafeAccessor] static externs for each tagged property's");
            sb.AppendLine("        // init-only setter. The runtime resolves these at JIT time to a");
            sb.AppendLine("        // direct callvirt against the property's set_ method, equivalent");
            sb.AppendLine("        // to a non-init setter call. AOT-clean.");
            // Pre-sort each group once and cache the per-group escaped identifier;
            // both blocks below iterate the same group in the same order.
            var sortedByType = byType
                .Select(g => (
                    TypeFqn: g.Key,
                    EscapedTypeName: EscapeIdent(g.Key),
                    Members: g.OrderBy(m => m.MemberName, StringComparer.Ordinal).ToList()))
                .ToList();
            foreach (var typeGroup in sortedByType)
            {
                string typeFqn = typeGroup.TypeFqn;
                string escapedTypeName = typeGroup.EscapedTypeName;
                foreach (var m in typeGroup.Members)
                {
                    // Get-only / computed members have no set_ accessor — skip
                    // the extern entirely (its target would never resolve) and
                    // let the emission loop below wire a throwing setter.
                    if (!m.HasSetter)
                        continue;
                    string accessorName = "__Set_" + escapedTypeName + "_" + m.MemberName;
                    sb.AppendLine($"        [global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.Method, Name = \"set_{m.MemberName}\")]");
                    sb.AppendLine($"        private static extern void {accessorName}({typeFqn} instance, {m.PropertyTypeFullName} value);");
                }
            }
            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Generated accessor table keyed by");
            sb.AppendLine("        /// <c>declaringTypeFqn + \"|\" + memberName</c>. Built once at");
            sb.AppendLine("        /// type-init; consulted by <c>DesignVariableBinder.AccessorFor</c>");
            sb.AppendLine("        /// before falling back to <c>Expression.Compile</c>. Total entries:");
            sb.AppendLine($"        /// {members.Length}.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        internal static readonly global::System.Collections.Generic.Dictionary<string, GeneratedAccessor>");
            sb.AppendLine("            GeneratedAccessors = new global::System.Collections.Generic.Dictionary<string, GeneratedAccessor>");
            sb.AppendLine("        {");

            foreach (var typeGroup in sortedByType)
            {
                string typeFqn = typeGroup.TypeFqn;
                string escapedTypeName = typeGroup.EscapedTypeName;
                foreach (var m in typeGroup.Members)
                {
                    string key = $"{typeFqn}|{m.MemberName}";
                    string accessorName = "__Set_" + escapedTypeName + "_" + m.MemberName;
                    string getterLambda =
                        $"static (object source) => (object?)(({typeFqn})source).{m.MemberName}";
                    // Settable members bind their [UnsafeAccessor] set_ extern;
                    // get-only/computed members (no set_ method) get a setter
                    // that throws a clear NotSupportedException instead of an
                    // extern that resolves to a MissingMethodException only when
                    // invoked. Such members are read-only SA dims whose sampled
                    // value must be applied through a hand-coded Unpack helper
                    // (e.g. AntennaLinkDesign.WithModulationIndex), never the
                    // registry setter path.
                    string setterLambda = m.HasSetter
                        ? $"static (object target, object value) => " +
                          $"{accessorName}(({typeFqn})target, ({m.PropertyTypeFullName})value)"
                        : $"static (object target, object value) => throw new global::System.NotSupportedException(" +
                          $"\"{EscapeString(typeFqn + "." + m.MemberName)} is a get-only [SaDesignVariable] (no setter); " +
                          $"apply its sampled value through the declaring type's hand-coded Unpack helper, not the registry setter path.\")";
                    string propType = m.PropertyTypeFullName;
                    sb.Append("            [\"");
                    sb.Append(EscapeString(key));
                    sb.AppendLine("\"] = new GeneratedAccessor(");
                    sb.Append("                getter: ");
                    sb.Append(getterLambda);
                    sb.AppendLine(",");
                    sb.Append("                setter: ");
                    sb.Append(setterLambda);
                    sb.AppendLine(",");
                    sb.Append("                propertyType: typeof(");
                    sb.Append(propType);
                    sb.AppendLine(")),");
                }
            }

            sb.AppendLine("        };");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            spc.AddSource(
                "DesignVariableBinder.Generated.g.cs",
                Microsoft.CodeAnalysis.Text.SourceText.From(sb.ToString(), Encoding.UTF8));
        }

        private static string EscapeString(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        // Convert an FQN like "global::Ns.Sub.Type" into a valid identifier suffix.
        private static string EscapeIdent(string fqn) =>
            fqn.Replace("global::", string.Empty).Replace(".", "_").Replace("+", "_");
    }
}
