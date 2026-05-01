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
	private SubViewport? _subViewport;
	private MeshInstance3D? _uiMesh;

	private MeshInstance3D? _sphereMeshInstance;
	private MeshInstance3D? _flatMeshInstance;
	private ShaderMaterial? _sphereMaterial;
	private ShaderMaterial? _flatMaterial;
	private Camera3D? _camera;

	private UIPointer? _rightPointer;

	private string? _videoPath;
	private string? _scriptPath;
	private VideoFormat _currentFormat = VideoFormat.Mono360;
	private double _playbackSpeed = 1.0;
	private int _maxPercentage = 100;
	private bool _isSyncing;

	private Label3D? _debugLabel;

	private static bool IsAndroid => OS.HasFeature("android");

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
		if (IsAndroid)
			OS.RequestPermissions();

		var xrInterface = XRServer.FindInterface("OpenXR");
		if (xrInterface != null && xrInterface.IsInitialized())
		{
			GetTree().Root.UseXR = true;
		}

		_videoManager = new VideoManager();
		AddChild(_videoManager);

		_funscriptPlayer = new FunscriptPlayerService();

		_headTracker = GetNodeOrNull<HeadTrackerService>("HeadTracker");

		_subViewport = GetNodeOrNull<SubViewport>("SubViewport");
		_uiMesh = GetNodeOrNull<MeshInstance3D>("UIMesh");

		_sphereMeshInstance = GetNodeOrNull<MeshInstance3D>("SphereMesh");
		_flatMeshInstance = GetNodeOrNull<MeshInstance3D>("FlatMesh");
		_camera = GetNodeOrNull<Camera3D>("XROrigin3D/XRCamera3D");
		_debugLabel = GetNodeOrNull<Label3D>("DebugLabel");

		SetupSphereMesh();
		SetupFlatMesh();
		SetupShaders();

		SetupUIMeshMaterial();
		SetupUIPointers();

	SetupOverlay();

		SetupVideoEvents();
		SetupFunscriptEvents();

		if (_camera != null)
			_camera.Current = true;

		if (_videoPath != null)
			LoadVideo(_videoPath);

		if (_scriptPath != null)
			_ = LoadScriptAsync(_scriptPath);
	}

	private void SetupUIMeshMaterial()
	{
		if (_uiMesh != null && _subViewport != null)
		{
			var quad = new QuadMesh();
			quad.Size = new Vector2(1.6f, 1.2f);
			_uiMesh.Mesh = quad;

			var mat = new StandardMaterial3D
			{
				ShadingMode = StandardMaterial3D.ShadingModeEnum.Unshaded,
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				AlbedoTexture = _subViewport.GetTexture(),
				CullMode = BaseMaterial3D.CullModeEnum.Disabled
			};
			_uiMesh.MaterialOverride = mat;
		}
	}

	private void SetupUIPointers()
	{
		_rightPointer = GetNodeOrNull<UIPointer>("XROrigin3D/XRController3DRight");
	}

	private void SetupOverlay()
	{
		_overlay = GetNodeOrNull<VrOverlay>("SubViewport/VrOverlay");
		if (_overlay == null)
		{
			GD.PrintErr("[VRPlayer] Failed to find VrOverlay under SubViewport.");
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

		bool isSphere = newFormat != VideoFormat.Flat;
		if (_sphereMeshInstance != null) _sphereMeshInstance.Visible = isSphere;
		if (_flatMeshInstance != null) _flatMeshInstance.Visible = !isSphere;

		_sphereMaterial?.SetShaderParameter("projection_mode", (int)newFormat);

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

	private void SetupSphereMesh()
	{
		if (_sphereMeshInstance == null) return;

		var sphere = new SphereMesh();
		sphere.Radius = 50.0f;
		sphere.Height = 100.0f;
		sphere.RadialSegments = 128;
		sphere.Rings = 64;
		_sphereMeshInstance.Mesh = sphere;
		_sphereMeshInstance.Visible = _currentFormat != VideoFormat.Flat;
	}

	private void SetupFlatMesh()
	{
		if (_flatMeshInstance == null) return;

		var quad = new QuadMesh();
		quad.Size = new Vector2(16.0f, 9.0f);
		_flatMeshInstance.Mesh = quad;
		_flatMeshInstance.Visible = _currentFormat == VideoFormat.Flat;
	}

	private void SetupShaders()
	{
		if (_sphereMeshInstance != null)
		{
			var sphereShader = ResourceLoader.Load<Shader>("res://Resources/Shaders/video_sphere.gdshader");
			if (sphereShader != null)
			{
				_sphereMaterial = new ShaderMaterial();
				_sphereMaterial.Shader = sphereShader;
				_sphereMaterial.SetShaderParameter("projection_mode", (int)_currentFormat);
				_sphereMeshInstance.MaterialOverride = _sphereMaterial;
			}
		}

		if (_flatMeshInstance != null)
		{
			var flatShader = ResourceLoader.Load<Shader>("res://Resources/Shaders/video_flat.gdshader");
			if (flatShader != null)
			{
				_flatMaterial = new ShaderMaterial();
				_flatMaterial.Shader = flatShader;
				_flatMeshInstance.MaterialOverride = _flatMaterial;
			}
		}

		_videoManager?.SetShaderMaterials(_sphereMaterial, _flatMaterial);
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
			AdjustMeshForVideo();
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

	private void LoadVideo(string path)
	{
		if (_videoManager == null) return;

		if (_debugLabel != null)
			_debugLabel.Text = "Loading: " + path.GetFile();

		_videoManager.LoadFile(path, _currentFormat);
		AdjustMeshForVideo();
		_overlay?.ResetHideTimer();

		if (_videoManager.State != PlaybackState.Error)
		{
			_videoManager.Play();
			if (_debugLabel != null)
				_debugLabel.Text = "Playing: " + path.GetFile();
		}
	}

	private void AdjustMeshForVideo()
	{
		if (_videoManager == null) return;
		int w = _videoManager.VideoWidth;
		int h = _videoManager.VideoHeight;
		if (w <= 0 || h <= 0) return;

		if (_currentFormat == VideoFormat.Flat && _flatMeshInstance != null)
		{
			float baseHeight = 9.0f;
			float newWidth = baseHeight * ((float)w / h);
			var quad = new QuadMesh();
			quad.Size = new Vector2(newWidth, baseHeight);
			_flatMeshInstance.Mesh = quad;
		}
	}

	public void PlayVideo(string absolutePath)
	{
		_videoPath = absolutePath;
		if (_videoManager != null)
			LoadVideo(absolutePath);
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
