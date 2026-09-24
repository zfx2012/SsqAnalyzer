using System.IO;
using System.Text.Json;

namespace SsqAnalyzer.Services.Kill;

public sealed class KillPagePreferences
{
    public int Window { get; set; } = 50;
    public string? SortKey { get; set; }
    public bool SortDescending { get; set; }
    public HashSet<string> Favorites { get; set; } = new();
    public Dictionary<string, double> ColumnWidths { get; set; } = new();
    public HashSet<string> HiddenColumns { get; set; } = new();
}

public sealed class KillPagePreferenceStore
{
    private readonly string _path;
    public KillPagePreferenceStore() : this(Path.Combine(AppPaths.DataDirectory, "kill_page_preferences.json")) { }
    internal KillPagePreferenceStore(string path) => _path = path;
    public KillPagePreferences Read()
    {
        var value = File.Exists(_path) ? JsonSerializer.Deserialize<KillPagePreferences>(File.ReadAllText(_path)) : new();
        if (value is null || value.Favorites is null || value.ColumnWidths is null || value.HiddenColumns is null)
            throw new InvalidDataException("列表偏好文件无效。");
        return value;
    }
    public void Save(KillPagePreferences value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(value)); File.Move(temporary, _path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
