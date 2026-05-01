using Godot;

namespace VRPlayerProject.Native;

public static class GoZenLib
{
    private static bool? _isAvailable;

    public static bool IsAvailable()
    {
        if (_isAvailable.HasValue)
            return _isAvailable.Value;

        try
        {
            var obj = ClassDB.Instantiate("GoZenVideo");
            _isAvailable = obj.VariantType != Variant.Type.Nil;
            if (_isAvailable.Value && obj.AsGodotObject() != null)
                obj.AsGodotObject().Free();
        }
        catch
        {
            _isAvailable = false;
        }

        return _isAvailable.Value;
    }

    public static GoZenVideo? CreateVideo()
    {
        try
        {
            return new GoZenVideo();
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[GoZenLib] Failed to create GoZenVideo: {ex.Message}");
            return null;
        }
    }

    public static Vector4 GetColorProfileVector(string profile)
    {
        return profile switch
        {
            "bt601" or "bt470" => new Vector4(1.402f, 0.344136f, 0.714136f, 1.772f),
            "bt2020" or "bt2100" => new Vector4(1.4746f, 0.16455f, 0.57135f, 1.8814f),
            _ => new Vector4(1.5748f, 0.1873f, 0.4681f, 1.8556f),
        };
    }
}
