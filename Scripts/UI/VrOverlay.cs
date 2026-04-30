using Godot;
using VRPlayerProject.Models;

namespace VRPlayerProject.UI;

public partial class VrOverlay : CanvasLayer
{
    private Button? _playPauseBtn;
    private Button? _stopBtn;
    private Button? _backBtn;
    private HSlider? _progressSlider;
    private Label? _timeLabel;
    private Label? _durationLabel;
    private Button? _speedBtn;
    private Button? _formatBtn;
    private HSlider? _maxSlider;
    private Label? _maxLabel;
    private Control? _overlayPanel;
    private Godot.Timer? _hideTimer;

    private bool _isPlaying;
    private double _positionMs;
    private double _durationMs = 1;
    private double _speed = 1.0;
    private VideoFormat _format = VideoFormat.Mono360;
    private int _maxPercentage = 100;
    private bool _visible = true;
    private bool _isDragging;

    private static readonly double[] Speeds = { 0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 2.0, 3.0, 4.0 };

    private static readonly VideoFormat[] Formats =
        { VideoFormat.Flat, VideoFormat.Mono180, VideoFormat.Fisheye180, VideoFormat.Mono360, VideoFormat.Stereo360 };
    private static readonly string[] FormatLabels =
        { "Flat", "180°", "Fish", "360°", "3D" };

    public event Action? OnPlayPause;
    public event Action? OnStop;
    public event Action? OnBack;
    public event Action<double>? OnSeek;
    public event Action<double>? OnSpeedChange;
    public event Action<VideoFormat>? OnFormatChange;
    public event Action<int>? OnMaxPercentageChange;

    public override void _Ready()
    {
        _playPauseBtn = GetNodeOrNull<Button>("Panel/Controls/PlayPauseBtn");
        _stopBtn = GetNodeOrNull<Button>("Panel/Controls/StopBtn");
        _backBtn = GetNodeOrNull<Button>("Panel/TopBar/BackBtn");
        _progressSlider = GetNodeOrNull<HSlider>("Panel/ProgressBar/Slider");
        _timeLabel = GetNodeOrNull<Label>("Panel/TopBar/TimeLabel");
        _durationLabel = GetNodeOrNull<Label>("Panel/TopBar/DurationLabel");
        _speedBtn = GetNodeOrNull<Button>("Panel/Controls/SpeedBtn");
        _formatBtn = GetNodeOrNull<Button>("Panel/TopBar/FormatBtn");
        _maxSlider = GetNodeOrNull<HSlider>("Panel/MaxSection/MaxSlider");
        _maxLabel = GetNodeOrNull<Label>("Panel/MaxSection/MaxLabel");
        _overlayPanel = GetNodeOrNull<Control>("Panel");

        _hideTimer = new Godot.Timer();
        _hideTimer.Name = "OverlayHideTimer";
        AddChild(_hideTimer);
        _hideTimer.OneShot = true;
        _hideTimer.Timeout += () =>
        {
            if (_isPlaying) ToggleVisible(false);
        };

        if (_playPauseBtn != null) _playPauseBtn.Pressed += () => OnPlayPause?.Invoke();
        if (_stopBtn != null) _stopBtn.Pressed += () => OnStop?.Invoke();
        if (_backBtn != null) _backBtn.Pressed += () => OnBack?.Invoke();

        if (_progressSlider != null)
        {
            _progressSlider.DragStarted += OnSliderDragStarted;
            _progressSlider.ValueChanged += OnSliderValueChanged;
            _progressSlider.DragEnded += OnSliderDragEnded;
        }

        if (_maxSlider != null)
        {
            _maxSlider.ValueChanged += (value) =>
            {
                _maxPercentage = (int)value;
                if (_maxLabel != null) _maxLabel.Text = $"{_maxPercentage}%";
                OnMaxPercentageChange?.Invoke(_maxPercentage);
                ResetHideTimer();
            };
        }

        if (_speedBtn != null)
        {
            _speedBtn.Pressed += () =>
            {
                int idx = System.Array.IndexOf(Speeds, _speed);
                _speed = Speeds[(idx + 1) % Speeds.Length];
                _speedBtn.Text = $"{_speed:F2}x";
                OnSpeedChange?.Invoke(_speed);
                ResetHideTimer();
            };
        }

        if (_formatBtn != null)
        {
            _formatBtn.Pressed += () =>
            {
                int idx = System.Array.IndexOf(Formats, _format);
                _format = Formats[(idx + 1) % Formats.Length];
                _formatBtn.Text = FormatLabels[(idx + 1) % Formats.Length];
                OnFormatChange?.Invoke(_format);
                ResetHideTimer();
            };
        }
    }

    public void SetFormat(VideoFormat format)
    {
        _format = format;
        int idx = System.Array.IndexOf(Formats, format);
        if (_formatBtn != null && idx >= 0)
            _formatBtn.Text = FormatLabels[idx];
    }

    public void UpdateState(bool isPlaying, double positionMs, double durationMs, double speed)
    {
        _isPlaying = isPlaying;
        _positionMs = positionMs;
        _durationMs = durationMs > 0 ? durationMs : 3600000;
        _speed = speed;

        if (_playPauseBtn != null)
            _playPauseBtn.Text = isPlaying ? "⏸" : "▶";

        if (_progressSlider != null)
        {
            _progressSlider.SetMax((float)_durationMs);
            if (!_isDragging)
                _progressSlider.SetValueNoSignal((float)positionMs);
        }

        if (_timeLabel != null) _timeLabel.Text = FormatTime(_isDragging ? (_progressSlider?.Value ?? positionMs) : positionMs);
        if (_durationLabel != null)
            _durationLabel.Text = durationMs > 0 ? FormatTime(durationMs) : "--:--";
        if (_speedBtn != null) _speedBtn.Text = $"{speed:F2}x";
    }

    private void OnSliderDragStarted()
    {
        _isDragging = true;
    }

    private void OnSliderValueChanged(double value)
    {
        if (_isDragging)
        {
            if (_timeLabel != null) _timeLabel.Text = FormatTime(value);
        }
        else
        {
            OnSeek?.Invoke(value);
        }
    }

    private void OnSliderDragEnded(bool valueChanged)
    {
        if (valueChanged)
            OnSeek?.Invoke(_progressSlider?.Value ?? 0);
        _isDragging = false;
    }

    public void ResetHideTimer()
    {
        ToggleVisible(true);
        if (_isPlaying) _hideTimer?.Start(3.0);
        else _hideTimer?.Stop();
    }

    private void ToggleVisible(bool show)
    {
        _visible = show;
        if (_overlayPanel != null) _overlayPanel.Visible = show;
    }

    public void Toggle()
    {
        ToggleVisible(!_visible);
        if (_visible) ResetHideTimer();
    }

    public override void _Input(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel") ||
            (@event is InputEventKey key && key.Keycode == Key.Space) ||
            (@event is InputEventJoypadButton joy && joy.ButtonIndex == JoyButton.B && joy.Pressed))
        {
            Toggle();
            GetViewport().SetInputAsHandled();
        }
    }

    private static string FormatTime(double ms)
    {
        int totalSec = (int)(ms / 1000);
        int min = totalSec / 60;
        int sec = totalSec % 60;
        return $"{min}:{sec:D2}";
    }
}
