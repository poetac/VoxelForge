// BlenderDiscovery.cs — locate blender.exe across common Windows install
// layouts so the renderer can call it without the user having to set PATH.
//
// Sprint render (2026-04-25) — Visual elegance / Noyron-parity track.

using System;
using System.Collections.Generic;
using System.IO;

namespace Voxelforge.Renderer;

internal static class BlenderDiscovery
{
    /// <summary>
    /// Environment variable that overrides auto-discovery. If set, used as-is.
    /// </summary>
    public const string EnvVarName = "VOXELFORGE_BLENDER_PATH";

    /// <summary>
    /// Returns the absolute path to a working blender.exe, or null if none
    /// found. Search order:
    ///   1. <see cref="EnvVarName"/> environment variable (full path to exe).
    ///   2. Common Windows install locations (Tools, Program Files).
    ///   3. blender.exe on PATH (last resort — `where blender`).
    /// </summary>
    public static string? Find()
    {
        // 1. Env var override.
        var envPath = Environment.GetEnvironmentVariable(EnvVarName);
        if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
        {
            return envPath;
        }

        // 2. Common install paths. Order matters — newest version preferred.
        // The double-nested layout (zip extracted into same-named folder)
        // is supported because that's the default behaviour of the Windows
        // explorer "Extract All" UX on a downloaded blender ZIP.
        var commonPaths = EnumerateCommonPaths();
        foreach (var p in commonPaths)
        {
            if (File.Exists(p)) return p;
        }

        // 3. PATH search. `where blender` semantics: the first matching exe
        // on PATH wins. Avoid running the full `where` subprocess; just
        // walk PATH ourselves to keep the discovery cheap and dependency-free.
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrEmpty(dir)) continue;
            var candidate = Path.Combine(dir, "blender.exe");
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    /// <summary>
    /// Diagnostic: returns the full list of paths that <see cref="Find"/>
    /// would consider, in order. Used by error messages so the user can see
    /// where to put blender.exe (or where it should already be).
    /// </summary>
    public static IReadOnlyList<string> SearchedPaths()
    {
        var list = new List<string>();
        var envPath = Environment.GetEnvironmentVariable(EnvVarName);
        if (!string.IsNullOrEmpty(envPath)) list.Add($"$env:{EnvVarName} = {envPath}");
        list.AddRange(EnumerateCommonPaths());
        list.Add("blender.exe on $PATH");
        return list;
    }

    private static IEnumerable<string> EnumerateCommonPaths()
    {
        // C:\Tools\Blender\... — manual portable extract location.
        // Two layouts: flattened (user moved contents up) and nested
        // (default ZIP extract behaviour).
        const string toolsRoot = @"C:\Tools\Blender";
        if (Directory.Exists(toolsRoot))
        {
            // Flattened: C:\Tools\Blender\blender.exe
            yield return Path.Combine(toolsRoot, "blender.exe");

            // Versioned subfolder: C:\Tools\Blender\blender-X.Y.Z-windows-x64\blender.exe
            // and the double-nested case: C:\Tools\Blender\blender-X.Y.Z-windows-x64\blender-X.Y.Z-windows-x64\blender.exe
            // Enumerate both depths.
            foreach (var sub in EnumerateBlenderSubfolders(toolsRoot, depth: 2))
            {
                yield return Path.Combine(sub, "blender.exe");
            }
        }

        // C:\Program Files\Blender Foundation\Blender X.Y\blender.exe — MSI installer default.
        const string progFilesRoot = @"C:\Program Files\Blender Foundation";
        if (Directory.Exists(progFilesRoot))
        {
            foreach (var sub in EnumerateBlenderSubfolders(progFilesRoot, depth: 1))
            {
                yield return Path.Combine(sub, "blender.exe");
            }
        }
    }

    private static IEnumerable<string> EnumerateBlenderSubfolders(string root, int depth)
    {
        if (depth <= 0 || !Directory.Exists(root)) yield break;
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            yield return dir;
            foreach (var nested in EnumerateBlenderSubfolders(dir, depth - 1))
            {
                yield return nested;
            }
        }
    }
}
