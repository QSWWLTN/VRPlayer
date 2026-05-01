using Godot;

namespace VRPlayerProject.Native;

public static class FFmpegLib
{
    private static bool? _isAvailable;

    public static bool IsAvailable()
    {
        if (_isAvailable.HasValue)
            return _isAvailable.Value;

        try
        {
            var obj = ClassDB.Instantiate("FFmpegVideoStream");
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

    public static VideoStream? CreateStream(string filePath)
    {
        try
        {
            var ffmpegObj = ClassDB.Instantiate("FFmpegVideoStream");
            if (ffmpegObj.VariantType == Variant.Type.Nil)
                return null;

            var ffmpegStream = ffmpegObj.AsGodotObject() as VideoStream;
            if (ffmpegStream == null)
                return null;

            ffmpegStream.Set("file", filePath);
            return ffmpegStream;
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[FFmpegLib] Failed to create FFmpegVideoStream: {ex.Message}");
            return null;
        }
    }
}
