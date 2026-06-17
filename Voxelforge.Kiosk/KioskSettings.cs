// KioskSettings.cs — JSON-persisted kiosk config.
//
// Watch folder path + next-sequence-number live in
// %LocalAppData%/Voxelforge.Kiosk/settings.json. The sequence number is
// recovered on startup from disk listings as well, so a settings file
// out-of-sync with the folder still picks the right next number.

using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Voxelforge.Kiosk;

public sealed class KioskSettings
{
    private static readonly JsonSerializerOptions s_jsonOpts = new()
    {
        WriteIndented = true,
    };

    public string WatchFolder { get; set; } = DefaultWatchFolder();
    public int    NextSequence { get; set; } = 1;

    public static string DefaultWatchFolder() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Voxelforge.Kiosk", "output");

    private static string SettingsPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Voxelforge.Kiosk", "settings.json");

    public static KioskSettings Load()
    {
        var path = SettingsPath();
        var s = File.Exists(path)
            ? JsonSerializer.Deserialize<KioskSettings>(File.ReadAllText(path)) ?? new KioskSettings()
            : new KioskSettings();

        // Reconcile sequence with what's actually on disk: pick max(file
        // sequence) + 1 across the watch folder. Defensive against the
        // settings file getting deleted between runs.
        Directory.CreateDirectory(s.WatchFolder);
        int observedMax = ScanFolderMaxSequence(s.WatchFolder);
        if (observedMax + 1 > s.NextSequence) s.NextSequence = observedMax + 1;

        return s;
    }

    public void Save()
    {
        var path = SettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Write to a sibling temp file then atomically replace, so a
        // crash mid-write doesn't corrupt the existing settings.json.
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(this, s_jsonOpts));
        if (File.Exists(path)) File.Replace(tempPath, path, destinationBackupFileName: null);
        else File.Move(tempPath, path);
    }

    private static readonly Regex KioskFileRx = new(
        @"^voxelforge_kiosk_(\d+)_", RegexOptions.IgnoreCase);

    private static int ScanFolderMaxSequence(string folder)
    {
        int max = 0;
        if (!Directory.Exists(folder)) return max;
        foreach (var f in Directory.EnumerateFiles(folder, "voxelforge_kiosk_*.stl"))
        {
            var m = KioskFileRx.Match(Path.GetFileName(f));
            if (m.Success && int.TryParse(m.Groups[1].Value, out var n) && n > max) max = n;
        }
        return max;
    }
}
