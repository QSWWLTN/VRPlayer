using Godot;

namespace VRPlayerProject.Services;

public class ScriptMatcherService
{
    public string? FindScript(string videoPath)
    {
        string dir = videoPath.GetBaseDir();
        string stem = videoPath.GetFile().GetBaseName();

        var candidates = new[]
        {
            $"{stem}.funscript",
            $"{stem}.L0.funscript",
            $"{stem}.json"
        };

        foreach (var name in candidates)
        {
            string path = $"{dir}/{name}";
            if (Godot.FileAccess.FileExists(path)) return ProjectSettings.GlobalizePath(path);
        }

        string scriptsDir = $"{dir}/Scripts";
        if (DirAccess.DirExistsAbsolute(scriptsDir))
        {
            foreach (var name in candidates)
            {
                string path = $"{scriptsDir}/{name}";
                if (Godot.FileAccess.FileExists(path)) return ProjectSettings.GlobalizePath(path);
            }
        }

        return null;
    }
}
