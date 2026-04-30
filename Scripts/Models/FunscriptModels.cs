using System.Text.Json;
using Godot;

namespace VRPlayerProject.Models;

public class FunscriptAction
{
    public int Pos { get; set; }
    public int At { get; set; }
}

public class FunscriptFile
{
    public string? Version { get; set; }
    public bool Inverted { get; set; }
    public int Range { get; set; } = 100;
    public double Fps { get; set; }
    public List<FunscriptAction> Actions { get; set; } = new();

    public double TotalDurationMs => Actions.Count > 0 ? Actions[^1].At : 0;

    public static FunscriptFile? FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var file = new FunscriptFile
        {
            Version = root.TryGetProperty("version", out var v) ? v.GetString() : null,
            Inverted = root.TryGetProperty("inverted", out var inv) && inv.GetBoolean(),
            Range = root.TryGetProperty("range", out var r) ? r.GetInt32() : 100,
            Fps = root.TryGetProperty("fps", out var fps) ? fps.GetDouble() : 0
        };

        if (root.TryGetProperty("actions", out var actions))
        {
            foreach (var item in actions.EnumerateArray())
            {
                file.Actions.Add(new FunscriptAction
                {
                    Pos = item.GetProperty("pos").GetInt32(),
                    At = item.GetProperty("at").GetInt32()
                });
            }
        }

        file.Actions.Sort((a, b) => a.At.CompareTo(b.At));
        return file;
    }

    public static async Task<FunscriptFile?> FromFileAsync(string path)
    {
        string json;
        using (var fa = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read))
        {
            if (fa == null)
            {
                GD.PrintErr($"[FunscriptFile] Cannot open: {path}");
                return null;
            }
            json = fa.GetAsText();
        }
        return await Task.Run(() => FromJson(json));
    }
}
