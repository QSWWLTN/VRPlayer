using Godot;
using Godot.Collections;

public partial class AudioStreamFFmpeg : AudioStream
{
    private Variant _backer;

    public AudioStreamFFmpeg()
    {
        _backer = ClassDB.Instantiate("AudioStreamFFmpeg");
    }

    public AudioStreamFFmpeg(Variant hndl)
    {
        _backer = hndl;
    }

    private GodotObject? Obj()
    {
        if (_backer.VariantType == Variant.Type.Nil)
            return null;
        return _backer.AsGodotObject();
    }

    public bool UseIcy
    {
        get => Obj()?.Get("use_icy").AsBool() ?? false;
        set => Obj()?.Set("use_icy", value);
    }

    public Error Open(string path, int streamIndex = -1) =>
        (Error)(Obj()?.Call("open", path, streamIndex).AsInt32() ?? -1);

    public Dictionary GetIcyHeaders() => Obj()?.Call("get_icy_headers").As<Dictionary>() ?? new Dictionary();
    public string GetStreamTitle() => Obj()?.Call("get_stream_title").AsString() ?? "";
    public Dictionary GetTags() => Obj()?.Call("get_tags").As<Dictionary>() ?? new Dictionary();

    public override AudioStreamPlayback _InstantiatePlayback() =>
        Obj()?.Call("__instantiate_playback").As<AudioStreamPlayback>();

    public override double _GetLength() => Obj()?.Call("get_length").AsDouble() ?? 0.0;
}
