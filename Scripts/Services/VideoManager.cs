using Godot;
using VRPlayerProject.Models;

namespace VRPlayerProject.Services;

public partial class VideoManager : Node
{
    private VideoStreamPlayer? _videoPlayer;
    private VideoStreamPlayer? _videoPlayerRight;
    private ShaderMaterial? _sphereMaterial;
    private ShaderMaterial? _flatMaterial;

    public VideoStreamPlayer? MainPlayer => _videoPlayer;
    public double DurationMs { get; private set; }
    public double PositionMs { get; private set; }
    public int VideoWidth { get; private set; }
    public int VideoHeight { get; private set; }

    public PlaybackState State { get; private set; } = PlaybackState.Idle;
    public VideoFormat CurrentFormat { get; private set; } = VideoFormat.Mono360;

    private double _seekTargetMs = -1;
    private bool _dimensionsRead;
    private int _durationReadAttempts;

    public event Action<double>? OnPositionChanged;
    public event Action<PlaybackState>? OnStateChanged;
    public event Action<string>? OnLog;
    public event Action<int, int>? OnDimensionsChanged;

    public override void _Ready()
    {
        GD.Print("[VideoManager] Initialized.");
    }

    public void SetupVideoPlayers(Node parent)
    {
        _videoPlayer = new VideoStreamPlayer();
        _videoPlayer.Name = "VideoPlayer";
        _videoPlayer.Size = new Vector2(1920, 1080);
        parent.AddChild(_videoPlayer);

        _videoPlayerRight = new VideoStreamPlayer();
        _videoPlayerRight.Name = "VideoPlayerRight";
        _videoPlayerRight.Size = new Vector2(1920, 1080);
        parent.AddChild(_videoPlayerRight);
        _videoPlayerRight.Visible = false;
    }

    public void SetSphereMaterial(ShaderMaterial material) => _sphereMaterial = material;
    public void SetFlatMaterial(ShaderMaterial material) => _flatMaterial = material;

    public void SwitchFormat(VideoFormat format)
    {
        CurrentFormat = format;
        if (_videoPlayerRight != null)
            _videoPlayerRight.Visible = format == VideoFormat.Stereo360;
    }

    public bool LoadFile(string path, VideoFormat format)
    {
        if (string.IsNullOrEmpty(path) || _videoPlayer == null)
        {
            SetErrorState();
            return false;
        }

        CurrentFormat = format;
        State = PlaybackState.Loading;
        OnStateChanged?.Invoke(State);

        SwitchFormat(format);

        _videoPlayer.Stop();
        _videoPlayer.Stream = null;
        _videoPlayerRight?.Stop();
        if (_videoPlayerRight != null) _videoPlayerRight.Stream = null;

        string? resolvedPath = ResolvePathForPlayer(path);
        if (resolvedPath == null)
        {
            OnLog?.Invoke($"File not found: {path}");
            SetErrorState();
            return false;
        }

        var stream = CreateVideoStream(resolvedPath, path);
        if (stream == null)
        {
            OnLog?.Invoke($"Cannot load video: {System.IO.Path.GetFileName(path)}");
            SetErrorState();
            return false;
        }

        _videoPlayer.Stream = stream;
        _videoPlayer.Autoplay = false;

        VideoWidth = 0;
        VideoHeight = 0;
        _dimensionsRead = false;

        if (CurrentFormat == VideoFormat.Stereo360 && _videoPlayerRight != null)
        {
            var streamR = CreateVideoStream(resolvedPath, path);
            if (streamR != null)
            {
                _videoPlayerRight.Stream = streamR;
                _videoPlayerRight.Autoplay = false;
            }
        }

        PositionMs = 0;
        DurationMs = 0;
        _durationReadAttempts = 0;

        State = PlaybackState.Paused;
        OnStateChanged?.Invoke(State);

        OnLog?.Invoke($"Loaded: {System.IO.Path.GetFileName(path)}");
        return true;
    }

    private bool TryReadDimensions()
    {
        if (_videoPlayer == null) return false;

        try
        {
            var tex = _videoPlayer.GetVideoTexture();
            if (tex != null)
            {
                int newW = tex.GetWidth();
                int newH = tex.GetHeight();
                if (newW > 0 && newH > 0)
                {
                    if (newW != VideoWidth || newH != VideoHeight)
                    {
                        VideoWidth = newW;
                        VideoHeight = newH;
                        GD.Print($"[VideoManager] Video dimensions: {VideoWidth}x{VideoHeight}");
                        OnDimensionsChanged?.Invoke(VideoWidth, VideoHeight);
                    }
                    return true;
                }
            }
        }
        catch { }

        return false;
    }

    private bool TryReadDuration()
    {
        if (_videoPlayer == null) return false;

        try
        {
            double lengthSec = _videoPlayer.GetStreamLength();
            if (lengthSec > 0)
            {
                double newDurMs = lengthSec * 1000.0;
                if (Math.Abs(newDurMs - DurationMs) > 100)
                {
                    DurationMs = newDurMs;
                    GD.Print($"[VideoManager] Duration: {lengthSec:F1}s");
                }
                return true;
            }
        }
        catch { }

        return false;
    }

    private void SetErrorState()
    {
        State = PlaybackState.Error;
        OnStateChanged?.Invoke(State);
    }

    private static string? ResolvePathForPlayer(string path)
    {
        if (Godot.FileAccess.FileExists(path))
            return ProjectSettings.GlobalizePath(path);

        try
        {
            string cacheDir = ProjectSettings.GlobalizePath("user://video_cache/");
            System.IO.Directory.CreateDirectory(cacheDir);
            string dest = System.IO.Path.Combine(cacheDir, System.IO.Path.GetFileName(path));
            if (System.IO.File.Exists(dest)) return dest;
        }
        catch { }

        return null;
    }

    private static VideoStream? CreateVideoStream(string filePath, string originalPath)
    {
        var ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
        bool isTheora = ext == ".ogv" || ext == ".ogg";

        string streamPath = filePath.Replace("\\", "/");

        if (HasUnicodeOrSpace(originalPath))
        {
            try
            {
                string cacheDir = ProjectSettings.GlobalizePath("user://video_cache/");
                System.IO.Directory.CreateDirectory(cacheDir);
                string safeName = $"__vr_{System.IO.Path.GetFileNameWithoutExtension(originalPath)}_{originalPath.GetHashCode():X8}{ext}";
                foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                    safeName = safeName.Replace(c.ToString(), "_");
                string dest = System.IO.Path.Combine(cacheDir, safeName);
                if (!System.IO.File.Exists(dest))
                {
                    using var src = Godot.FileAccess.Open(originalPath, Godot.FileAccess.ModeFlags.Read);
                    if (src != null)
                    {
                        var data = src.GetBuffer((long)src.GetLength());
                        System.IO.File.WriteAllBytes(dest, data);
                    }
                }
                streamPath = $"user://video_cache/{safeName}";
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[VideoManager] Path copy failed: {ex.Message}");
            }
        }

        if (isTheora)
        {
            var t = new VideoStreamTheora();
            t.File = streamPath;
            return t;
        }

        try
        {
            var ffmpegObj = Godot.ClassDB.Instantiate("FFmpegVideoStream");
            if (ffmpegObj.VariantType != Variant.Type.Nil)
            {
                var ffmpegStream = ffmpegObj.AsGodotObject() as VideoStream;
                if (ffmpegStream != null)
                {
                    ffmpegStream.Set("file", streamPath);
                    return ffmpegStream;
                }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[VideoManager] FFmpegVideoStream creation failed: {ex.Message}");
        }

        try
        {
            var videoStream = ResourceLoader.Load<VideoStream>(streamPath);
            if (videoStream != null) return videoStream;
        }
        catch { }

        GD.PrintErr($"[VideoManager] Cannot create stream for: {filePath}");
        return null;
    }

    private static bool HasUnicodeOrSpace(string path)
    {
        foreach (char c in path)
        {
            if (c > 127 || c == ' ') return true;
        }
        return false;
    }

    public void Play()
    {
        if (_videoPlayer?.Stream == null)
        {
            SetErrorState();
            return;
        }

        _videoPlayer.Play();
        _videoPlayerRight?.Play();
        State = PlaybackState.Playing;
        OnStateChanged?.Invoke(State);
    }

    public void Pause()
    {
        if (_videoPlayer != null)
            _videoPlayer.Paused = true;
        if (_videoPlayerRight != null)
            _videoPlayerRight.Paused = true;
        State = PlaybackState.Paused;
        OnStateChanged?.Invoke(State);
    }

    public void Resume()
    {
        if (_videoPlayer?.Stream == null) return;

        if (_videoPlayer != null)
            _videoPlayer.Paused = false;
        if (_videoPlayerRight != null)
            _videoPlayerRight.Paused = false;
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
        _videoPlayer?.Stop();
        _videoPlayerRight?.Stop();
        State = PlaybackState.Idle;
        OnStateChanged?.Invoke(State);
    }

    public void SeekTo(double ms)
    {
        double seconds = ms / 1000.0;
        if (_videoPlayer != null) _videoPlayer.StreamPosition = seconds;
        if (_videoPlayerRight != null) _videoPlayerRight.StreamPosition = seconds;
        PositionMs = ms;
        _seekTargetMs = ms;
        OnPositionChanged?.Invoke(PositionMs);
    }

    public void SetSpeed(double speed)
    {
        if (_videoPlayer != null)
            _videoPlayer.SpeedScale = (float)speed;
        if (_videoPlayerRight != null)
            _videoPlayerRight.SpeedScale = (float)speed;
    }

    public override void _Process(double delta)
    {
        if (_videoPlayer == null) return;

        if (_videoPlayer.Stream != null)
        {
            var tex = _videoPlayer.GetVideoTexture();
            if (tex != null)
            {
                if (_sphereMaterial != null)
                    _sphereMaterial.SetShaderParameter("video_texture", tex);
                if (_flatMaterial != null)
                    _flatMaterial.SetShaderParameter("video_texture", tex);
            }
        }

        if (State == PlaybackState.Playing || (State == PlaybackState.Paused && _seekTargetMs >= 0))
        {
            double posSec = _videoPlayer.GetStreamPosition();
            double newPosMs = posSec * 1000.0;

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

            if (!_videoPlayer.IsPlaying())
            {
                State = PlaybackState.Ended;
                OnStateChanged?.Invoke(State);
            }
        }

        if (!_dimensionsRead && _videoPlayer.Stream != null && State != PlaybackState.Idle)
        {
            _dimensionsRead = TryReadDimensions();
        }

        if (DurationMs <= 0 && _videoPlayer.Stream != null && State != PlaybackState.Idle)
        {
            _durationReadAttempts++;
            TryReadDuration();
        }
    }

    public override void _ExitTree()
    {
        Stop();
    }
}
