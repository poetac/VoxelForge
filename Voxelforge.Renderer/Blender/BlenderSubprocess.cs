// BlenderSubprocess.cs — extracted Blender headless invocation.
// Sprint Team-V Wave 1 (2026-05-05): refactored out of Program.cs so
// OrbitRig can share the same subprocess contract.

using System.Diagnostics;
using System.Text.Json;

namespace Voxelforge.Renderer.Blender;

internal static class BlenderSubprocess
{
    private static readonly JsonSerializerOptions PayloadJsonOpts = new(JsonSerializerDefaults.Web);

    internal record RenderPayload(
        string  InputStl,
        string  OutputPath,
        string  MaterialPath,
        int     Width,
        int     Height,
        int     Samples,
        string  Engine,
        string  Mode,
        int     Frames,
        string? HdriPath = null);

    // Returns Blender's exit code (0 = success).
    // Does NOT redirect Blender's stdout/stderr so progress prints reach the terminal.
    internal static int Run(string blenderExe, string renderScript, RenderPayload p)
    {
        string payloadJson = JsonSerializer.Serialize(new
        {
            input_stl     = p.InputStl,
            output_path   = p.OutputPath,
            material_path = p.MaterialPath,
            hdri_path     = p.HdriPath,   // null → Python fallback (grey-blue or bundled studio.exr)
            resolution    = new { width = p.Width, height = p.Height, samples = p.Samples, engine = p.Engine },
            mode          = p.Mode,
            frames        = p.Frames,
        }, PayloadJsonOpts);

        var psi = new ProcessStartInfo
        {
            FileName        = blenderExe,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("--background");
        psi.ArgumentList.Add("--python");
        psi.ArgumentList.Add(renderScript);
        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add(payloadJson);

        using var proc = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException($"Process.Start returned null for {blenderExe}");
        proc.WaitForExit();
        return proc.ExitCode;
    }
}
