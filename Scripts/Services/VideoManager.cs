using Godot;
using VRPlayerProject.Models;

namespace VRPlayerProject.Services;

public partial class VideoManager : Node
{
    private Node? _exoPlayer;
    private OpenXRCompositionLayerEquirect? _compositionLayer;
    private int _playerId = -1;

    private bool _exoReady;

    public int MainPlayerId => _playerId;
    public double DurationMs { get; private set; }
    public double PositionMs { get; private set; }
    public int VideoWidth { get; private set; }
    public int VideoHeight { get; private set; }

    public PlaybackState State { get; private set; } = PlaybackState.Idle;
    public VideoFormat CurrentFormat { get; private set; } = VideoFormat.Mono360;

    private double _seekTargetMs = -1;
    private bool _dimensionsRead;
    private bool _playRequested;

    public event Action<double>? OnPositionChanged;
    public event Action<PlaybackState>? OnStateChanged;
    public event Action<string>? OnLog;
    public event Action<int, int>? OnDimensionsChanged;

    public override void _Ready()
    {
        GD.Print("[VideoManager] Initialized.");
    }

    public void SetupVideoPlayers(Node parent, OpenXRCompositionLayerEquirect compositionLayer)
    {
        _compositionLayer = compositionLayer;

        _exoPlayer = parent.GetNodeOrNull("/root/ExoPlayer");
        if (_exoPlayer == null)
        {
            GD.PrintErr("[VideoManager] ExoPlayer autoload not found. Is the plugin enabled?");
            return;
        }

        ConnectExoPlayerSignals();
        GD.Print("[VideoManager] ExoPlayer integration ready.");
    }

    public void SwitchFormat(VideoFormat format)
    {
        CurrentFormat = format;
    }

    public bool LoadFile(string path, VideoFormat format)
    {
        if (string.IsNullOrEmpty(path) || _exoPlayer == null || _compositionLayer == null)
        {
            SetErrorState();
            return false;
        }

        ReleaseCurrentPlayer();

        CurrentFormat = format;
        State = PlaybackState.Loading;
        OnStateChanged?.Invoke(State);

        var surface = _compositionLayer.Call("get_surface");
        if (surface.VariantType == Variant.Type.Nil)
        {
            OnLog?.Invoke("Failed to get Android surface from composition layer.");
            SetErrorState();
            return false;
        }

        string uri = ConvertToUri(path);
        var result = _exoPlayer.Call("create_exoplayer_instance", surface, uri);
        int id = result.AsInt32();

        if (id <= 0)
        {
            OnLog?.Invoke($"Failed to create ExoPlayer instance for: {path.GetFile()}");
            SetErrorState();
            return false;
        }

        _playerId = id;
        _exoReady = false;
        _playRequested = false;
        VideoWidth = 0;
        VideoHeight = 0;
        _dimensionsRead = false;
        PositionMs = 0;
        DurationMs = 0;

        OnLog?.Invoke($"Loaded: {path.GetFile()}");

        return true;
    }

    public void Play()
    {
        if (_playerId <= 0 || _exoPlayer == null)
        {
            SetErrorState();
            return;
        }

        _exoPlayer.Call("play", _playerId);
        _playRequested = true;

        if (_exoReady)
        {
            State = PlaybackState.Playing;
            OnStateChanged?.Invoke(State);
        }
    }

    public void Pause()
    {
        if (_playerId <= 0 || _exoPlayer == null) return;

        _exoPlayer.Call("pause", _playerId);
        State = PlaybackState.Paused;
        OnStateChanged?.Invoke(State);
    }

    public void Resume()
    {
        if (_playerId <= 0 || _exoPlayer == null) return;

        _exoPlayer.Call("play", _playerId);
        State = PlaybackState.Playing;
        OnStateChanged?.Invoke(State);
    }

    public void TogglePlayPause()
    {
        if (State == PlaybackState.Playing) Pause();
        else Resume();
    }

    public void Stop()
    {
        if (_playerId > 0 && _exoPlayer != null)
        {
            _exoPlayer.Call("pause", _playerId);
        }
        ReleaseCurrentPlayer();
        State = PlaybackState.Idle;
        OnStateChanged?.Invoke(State);
    }

    public void SeekTo(double ms)
    {
        if (_playerId <= 0 || _exoPlayer == null) return;

        long positionMs = (long)ms;
        _exoPlayer.Call("seekTo", _playerId, positionMs);
        PositionMs = ms;
        _seekTargetMs = ms;
        OnPositionChanged?.Invoke(PositionMs);
    }

    public void SetSpeed(double speed)
    {
        if (_playerId <= 0 || _exoPlayer == null) return;

        try
        {
            _exoPlayer.Call("setPlaybackSpeed", _playerId, speed);
        }
        catch
        {
            GD.Print("[VideoManager] setPlaybackSpeed not supported by plugin.");
        }
    }

    public override void _Process(double delta)
    {
        if (_playerId <= 0 || _exoPlayer == null) return;

        if (State == PlaybackState.Playing || State == PlaybackState.Paused)
        {
            try
            {
                double newPosMs = (double)_exoPlayer.Call("getCurrentPlaybackPosition", _playerId);

                if (_seekTargetMs >= 0)
                {
                    if (Math.Abs(newPosMs - _seekTargetMs) < 500)
                    {
                        _seekTargetMs = -1;
                        PositionMs = newPosMs;
                    }
                    else
                    {
                        PositionMs = _seekTargetMs;
                    }
                }
                else
                {
                    PositionMs = newPosMs;
                }

                OnPositionChanged?.Invoke(PositionMs);

                if (State == PlaybackState.Paused)
                    return;

                if (DurationMs > 0 && PositionMs >= DurationMs - 200)
                {
                    State = PlaybackState.Ended;
                    OnStateChanged?.Invoke(State);
                }
            }
            catch
            {
                // Silently retry on next frame
            }
        }

        if (!_dimensionsRead && _exoReady)
        {
            _dimensionsRead = TryReadDimensions();
        }
    }

    private void ReleaseCurrentPlayer()
    {
        if (_playerId > 0 && _exoPlayer != null)
        {
            try
            {
                _exoPlayer.Call("release_player", _playerId);
            }
            catch { }
        }
        _playerId = -1;
        _exoReady = false;
        _playRequested = false;
    }

    private bool TryReadDimensions()
    {
        if (_playerId <= 0 || _exoPlayer == null) return false;

        try
        {
            var resolutions = _exoPlayer.Call("getVideoResolutions", _playerId);
            var dict = resolutions.AsGodotDictionary();
            if (dict != null && dict.Count > 0)
            {
                foreach (var key in dict.Keys)
                {
                    var dims = dict[key].AsString();
                    var parts = dims?.Split('x');
                    if (parts != null && parts.Length == 2 &&
                        int.TryParse(parts[0], out int w) &&
                        int.TryParse(parts[1], out int h))
                    {
                        if (w > 0 && h > 0 && (w != VideoWidth || h != VideoHeight))
                        {
                            VideoWidth = w;
                            VideoHeight = h;
                            GD.Print($"[VideoManager] Video dimensions: {VideoWidth}x{VideoHeight}");
                            OnDimensionsChanged?.Invoke(VideoWidth, VideoHeight);
                            return true;
                        }
                    }
                }
            }
        }
        catch { }

        return false;
    }

    public override void _ExitTree()
    {
        ReleaseCurrentPlayer();
    }

    private void ConnectExoPlayerSignals()
    {
        if (_exoPlayer == null) return;

        _exoPlayer.Connect("player_ready", Callable.From<long, double>((id, durationMs) =>
        {
            if ((int)id == _playerId)
            {
                _exoReady = true;
                DurationMs = durationMs;
                GD.Print($"[VideoManager] Player ready. Duration: {durationMs}ms");

                if (_playRequested)
                {
                    State = PlaybackState.Playing;
                }
                else
                {
                    State = PlaybackState.Paused;
                }
                OnStateChanged?.Invoke(State);
            }
        }));

        _exoPlayer.Connect("player_error", Callable.From<long, string>((id, errorMsg) =>
        {
            if ((int)id == _playerId)
            {
                GD.PrintErr($"[VideoManager] ExoPlayer error: {errorMsg}");
                OnLog?.Invoke($"Error: {errorMsg}");
                SetErrorState();
            }
        }));

        _exoPlayer.Connect("video_end", Callable.From<long>((id) =>
        {
            if ((int)id == _playerId)
            {
                State = PlaybackState.Ended;
                OnStateChanged?.Invoke(State);
            }
        }));
    }

    private void SetErrorState()
    {
        State = PlaybackState.Error;
        OnStateChanged?.Invoke(State);
    }

    private static string ConvertToUri(string path)
    {
        if (path.StartsWith("http://") || path.StartsWith("https://") || path.StartsWith("file://"))
            return path;

        if (path.StartsWith("res://") || path.StartsWith("user://"))
            path = ProjectSettings.GlobalizePath(path);

        path = path.Replace("\\", "/");

        if (!path.StartsWith("/"))
            path = "/" + path;

        return "file://" + path;
    }
}
