using Godot;
using System.Threading.Tasks;
using VRPlayerProject.Models;
using VRPlayerProject.Services;
using VRPlayerProject.UI;

namespace VRPlayerProject;

public partial class VRPlayer : Node
{
	private VideoManager? _videoManager;
	private FunscriptPlayerService? _funscriptPlayer;
	private OutputManager? _outputManager;
	private HeadTrackerService? _headTracker;
	private VrOverlay? _overlay;
	private VrPanel3D? _uiPanel;

	private Camera3D? _camera;
	private OpenXRCompositionLayerEquirect? _compositionLayer;

	private string? _videoPath;
	private string? _scriptPath;
	private VideoFormat _currentFormat = VideoFormat.Mono360;
	private double _playbackSpeed = 1.0;
	private int _maxPercentage = 100;
	private bool _isSyncing;

	private Label3D? _debugLabel;

	public void Initialize(
		string videoPath,
		string? scriptPath = null,
		VideoFormat format = VideoFormat.Mono360,
		SerialOutputService? serialOutput = null,
		WebSocketOutputService? webSocketOutput = null)
	{
		_videoPath = videoPath;
		_scriptPath = scriptPath;
		_currentFormat = format;

		var serial = serialOutput ?? new SerialOutputService();
		var ws = webSocketOutput ?? new WebSocketOutputService();
		_outputManager = new OutputManager(serial, ws);
	}

	public override void _Ready()
	{
		var xrInterface = XRServer.FindInterface("OpenXR");
		if (xrInterface != null && xrInterface.IsInitialized())
		{
			GetTree().Root.UseXR = true;
		}

		_videoManager = new VideoManager();
		AddChild(_videoManager);

		_funscriptPlayer = new FunscriptPlayerService();

		_headTracker = GetNodeOrNull<HeadTrackerService>("HeadTracker");

		if (xrInterface != null && xrInterface.IsInitialized())
		{
			_headTracker?.SetEnabled(false);
		}

		_uiPanel = GetNodeOrNull<VrPanel3D>("VrPanel3D");

		_camera = GetNodeOrNull<Camera3D>("XROrigin3D/XRCamera3D");
		_debugLabel = GetNodeOrNull<Label3D>("DebugLabel");

		_compositionLayer = GetNodeOrNull<OpenXRCompositionLayerEquirect>("VideoLayer");
		if (_compositionLayer == null)
			GD.PrintErr("[VRPlayer] VideoLayer (OpenXRCompositionLayerEquirect) not found in scene.");

		if (OS.HasFeature("android"))
			_videoManager.SetupVideoPlayers(this, _compositionLayer!);
		else
			GD.Print("[VRPlayer] Not running on Android; ExoPlayer bridge skipped.");

		SetupOverlay();

		SetupVideoEvents();
		SetupFunscriptEvents();

		if (_camera != null)
			_camera.Current = true;

		if (_videoPath != null)
			PlayVideo(_videoPath);

		if (_scriptPath != null)
			_ = LoadScriptAsync(_scriptPath);
	}

	public void PlayVideo(string absolutePath)
	{
		if (_videoManager == null) return;

		string uri = absolutePath;
		if (!uri.StartsWith("file://") && !uri.StartsWith("http"))
		{
			if (uri.StartsWith("res://") || uri.StartsWith("user://"))
				uri = ProjectSettings.GlobalizePath(uri);
			uri = uri.Replace("\\", "/");
			if (!uri.StartsWith("/"))
				uri = "/" + uri;
			uri = "file://" + uri;
		}

		if (_debugLabel != null)
			_debugLabel.Text = "Loading: " + absolutePath.GetFile();

		_videoManager.LoadFile(absolutePath, _currentFormat);
		_overlay?.ResetHideTimer();

		if (_videoManager.State != PlaybackState.Error)
		{
			_videoManager.Play();
			if (_debugLabel != null)
				_debugLabel.Text = "Playing: " + absolutePath.GetFile();
		}
	}

	private void SetupOverlay()
	{
		if (_uiPanel == null) return;

		var overlayScene = ResourceLoader.Load<PackedScene>("res://Scenes/VrOverlay.tscn");
		if (overlayScene == null) return;

		var overlayControl = overlayScene.Instantiate<Control>();
		_uiPanel.SetContent(overlayControl);

		_overlay = overlayControl as VrOverlay;
		if (_overlay == null)
			_overlay = overlayControl.GetNodeOrNull<VrOverlay>(".");

		if (_overlay == null)
		{
			GD.PrintErr("[VRPlayer] Failed to get VrOverlay from instantiated scene.");
			return;
		}

		_overlay.OnPlayPause += () =>
		{
			_videoManager?.TogglePlayPause();
			_overlay.ResetHideTimer();
		};

		_overlay.OnStop += () =>
		{
			_videoManager?.Stop();
			_funscriptPlayer?.SetActions(null);
			GetTree().ChangeSceneToFile("res://Scenes/Main3D.tscn");
		};

		_overlay.OnBack += () =>
		{
			_videoManager?.Stop();
			GetTree().ChangeSceneToFile("res://Scenes/Main3D.tscn");
		};

		_overlay.OnSeek += (ms) =>
		{
			_videoManager?.SeekTo(ms);
			_funscriptPlayer?.SeekTo(ms);
			_overlay.ResetHideTimer();
		};

		_overlay.OnSpeedChange += (speed) =>
		{
			_playbackSpeed = speed;
			_videoManager?.SetSpeed(speed);
			_funscriptPlayer?.SetSpeedFactor(speed);
			_overlay.ResetHideTimer();
		};

		_overlay.OnFormatChange += (format) =>
		{
			SwitchFormat(format);
			_overlay.ResetHideTimer();
		};

		_overlay.OnMaxPercentageChange += (value) =>
		{
			SetMaxPercentage(value);
		};

		_overlay.SetFormat(_currentFormat);
	}

	public void SwitchFormat(VideoFormat newFormat)
	{
		_currentFormat = newFormat;
		_videoManager?.SwitchFormat(newFormat);

		_overlay?.SetFormat(newFormat);
		_overlay?.ResetHideTimer();
		_overlay?.UpdateState(
			_videoManager?.State == PlaybackState.Playing,
			_videoManager?.PositionMs ?? 0,
			_videoManager?.DurationMs ?? 0,
			_playbackSpeed
		);
	}

	public void SetMaxPercentage(int value)
	{
		_maxPercentage = value;
		_funscriptPlayer?.SetMaxPercentage(value);
		_outputManager?.SetMaxPercentage(value);
	}

	private void SetupVideoEvents()
	{
		if (_videoManager == null) return;

		_videoManager.OnPositionChanged += (posMs) =>
		{
			_isSyncing = _videoManager.State == PlaybackState.Playing
						 && _funscriptPlayer?.Actions is { Count: > 0 };

			if (_isSyncing)
				_funscriptPlayer!.SyncToPosition(posMs);

			_overlay?.UpdateState(
				_videoManager.State == PlaybackState.Playing,
				posMs,
				_videoManager.DurationMs,
				_playbackSpeed
			);
		};

		_videoManager.OnStateChanged += (state) =>
		{
			if (state == PlaybackState.Error && _debugLabel != null)
				_debugLabel.Text = "Error: Cannot play video file.\nUnsupported format or codec.";
		};

		_videoManager.OnLog += (msg) =>
		{
			if (_debugLabel != null)
				_debugLabel.Text = msg;
			GD.Print($"[VideoManager] {msg}");
		};

		_videoManager.OnDimensionsChanged += (w, h) =>
		{
		};
	}

	private void SetupFunscriptEvents()
	{
		if (_funscriptPlayer == null || _outputManager == null) return;

		_funscriptPlayer.OnOutputPosition += (pos) =>
		{
			_outputManager.SendPosition(pos);
		};
	}

	public override void _Input(InputEvent @event)
	{
		if (@event.IsActionPressed("ui_cancel") ||
			(@event is InputEventKey key && key.Keycode == Key.Space && key.Pressed) ||
			(@event is InputEventJoypadButton joy && joy.ButtonIndex == JoyButton.B && joy.Pressed))
		{
			_overlay?.ToggleVisibility();
		}
	}

	private async Task LoadScriptAsync(string path)
	{
		var script = await FunscriptFile.FromFileAsync(path);
		if (script != null)
		{
			_funscriptPlayer?.SetActions(script.Actions);
			_isSyncing = true;
			if (_debugLabel != null)
				_debugLabel.Text += $"\nScript: {path.GetFile()} ({script.Actions.Count} actions)";
		}
	}

	public override void _ExitTree()
	{
		_outputManager?.Dispose();
	}
}
