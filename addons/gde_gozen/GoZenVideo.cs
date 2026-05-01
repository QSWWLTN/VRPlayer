using Godot;
using Godot.Collections;

public partial class GoZenVideo : Resource
{
    private Variant _gdHndl;

    public GoZenVideo()
    {
        _gdHndl = ClassDB.Instantiate("GoZenVideo");
    }

    public GoZenVideo(Variant hndl)
    {
        _gdHndl = hndl;
    }

    private GodotObject? Obj()
    {
        if (_gdHndl.VariantType == Variant.Type.Nil)
            return null;
        return _gdHndl.AsGodotObject();
    }

    public static Dictionary GetFileMeta(string file_path) =>
        ClassDB.ClassCallStatic("GoZenVideo", "get_file_meta", Variant.CreateFrom(file_path)).AsGodotDictionary();

    public int Open(string video_path) => Obj()?.Call("open", Variant.CreateFrom(video_path)).AsInt32() ?? -1;
    public void Close() => Obj()?.Call("close");
    public bool IsOpen() => Obj()?.Call("is_open").AsBool() ?? false;

    public int SeekFrame(int frame_nr) => Obj()?.Call("seek_frame", Variant.CreateFrom(frame_nr)).AsInt32() ?? -1;
    public bool NextFrame(bool skip = false) => Obj()?.Call("next_frame", Variant.CreateFrom(skip)).AsBool() ?? false;

    public int[] GetStreams(int streamType) => Obj()?.Call("get_streams", Variant.CreateFrom(streamType)).AsInt32Array() ?? [];
    public Dictionary GetStreamMetadata(int streamIndex) => Obj()?.Call("get_stream_metadata", Variant.CreateFrom(streamIndex)).AsGodotDictionary() ?? new Dictionary();

    public int GetChapterCount() => Obj()?.Call("get_chapter_count").AsInt32() ?? 0;
    public float GetChapterStart(int chapterIndex) => Obj()?.Call("get_chapter_start", Variant.CreateFrom(chapterIndex)).AsSingle() ?? 0f;
    public float GetChapterEnd(int chapterIndex) => Obj()?.Call("get_chapter_end", Variant.CreateFrom(chapterIndex)).AsSingle() ?? 0f;
    public Dictionary GetChapterMetadata(int chapterIndex) => Obj()?.Call("get_chapter_metadata", Variant.CreateFrom(chapterIndex)).AsGodotDictionary() ?? new Dictionary();

    public string GetPath() => Obj()?.Call("get_path").AsString() ?? "";
    public float GetFramerate() => Obj()?.Call("get_framerate").AsSingle() ?? 30f;
    public int GetFrameCount() => Obj()?.Call("get_frame_count").AsInt32() ?? 0;
    public Vector2I GetResolution() => Obj()?.Call("get_resolution").AsVector2I() ?? Vector2I.Zero;
    public Vector2I GetActualResolution() => Obj()?.Call("get_actual_resolution").AsVector2I() ?? Vector2I.Zero;
    public int GetWidth() => Obj()?.Call("get_width").AsInt32() ?? 0;
    public int GetHeight() => Obj()?.Call("get_height").AsInt32() ?? 0;
    public int GetPadding() => Obj()?.Call("get_padding").AsInt32() ?? 0;
    public int GetRotation() => Obj()?.Call("get_rotation").AsInt32() ?? 0;
    public int GetInterlaced() => Obj()?.Call("get_interlaced").AsInt32() ?? 0;
    public float GetSar() => Obj()?.Call("get_sar").AsSingle() ?? 0f;

    public void EnableDebug() => Obj()?.Call("enable_debug");
    public void DisableDebug() => Obj()?.Call("disable_debug");
    public bool GetDebugEnabled() => Obj()?.Call("get_debug_enabled").AsBool() ?? false;

    public string GetPixelFormat() => Obj()?.Call("get_pixel_format").AsString() ?? "";
    public string GetColorProfile() => Obj()?.Call("get_color_profile").AsString() ?? "";

    public bool GetHasAlpha() => Obj()?.Call("get_has_alpha").AsBool() ?? false;
    public bool IsFullColorRange() => Obj()?.Call("is_full_color_range").AsBool() ?? true;
    public bool IsUsingSws() => Obj()?.Call("is_using_sws").AsBool() ?? false;

    public Image GetYData() => Obj()?.Call("get_y_data").As<Image>();
    public Image GetUData() => Obj()?.Call("get_u_data").As<Image>();
    public Image GetVData() => Obj()?.Call("get_v_data").As<Image>();
    public Image GetAData() => Obj()?.Call("get_a_data").As<Image>();
}
