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
    private XRController3D? _leftController;

    private bool _wasMenuPressed; 
    private int _framesSinceStart = 0;

    private string? _videoPath;
    private string? _scriptPath;
    private VideoFormat _currentFormat = VideoFormat.Mono360;
    private double _playbackSpeed = 1.0;
    private int _maxPercentage = 100;
    private bool _isSyncing;
    
    // --- 过冲开关参数与视频俯仰角缓存 ---
    private bool _enableOvershoot;
    private float _videoPitchOffset = 0.0f;
    private Basis _flatBaseBasis = Basis.Identity;

    private static bool IsAndroid => OS.HasFeature("android");

    public void Initialize(
        string videoPath,
        string? scriptPath = null,
        VideoFormat format = VideoFormat.Mono360,
        SerialOutputService? serialOutput = null,
        WebSocketOutputService? webSocketOutput = null,
        bool enableOvershoot = false)
    {
        _videoPath = videoPath;
        _scriptPath = scriptPath;
        _currentFormat = format;
        _enableOvershoot = enableOvershoot;

        var serial = serialOutput ?? new SerialOutputService();
        var ws = webSocketOutput ?? new WebSocketOutputService();
        _outputManager = new OutputManager(serial, ws);
    }

    public override void _Ready()
    {
        if (IsAndroid) OS.RequestPermissions();

        var xrInterface = XRServer.FindInterface("OpenXR");
        if (xrInterface != null && xrInterface.IsInitialized())
        {
            GetTree().Root.UseXR = true;
        }

        _videoManager = new VideoManager();
        AddChild(_videoManager);

        _funscriptPlayer = new FunscriptPlayerService();
        _funscriptPlayer.SetOvershootEnabled(_enableOvershoot); // 注入过冲曲线配置

        _headTracker = GetNodeOrNull<HeadTrackerService>("HeadTracker");
        _subViewport = GetNodeOrNull<SubViewport>("SubViewport");
        _uiMesh = GetNodeOrNull<MeshInstance3D>("UIMesh");
        _sphereMeshInstance = GetNodeOrNull<MeshInstance3D>("SphereMesh");
        _flatMeshInstance = GetNodeOrNull<MeshInstance3D>("FlatMesh");
        _camera = GetNodeOrNull<Camera3D>("XROrigin3D/XRCamera3D");

        SetupSphereMesh();
        SetupFlatMesh();
        SetupShaders();
        SetupUIMeshMaterial();
        SetupUIPointers();
        SetupOverlay();
        SetupVideoEvents();
        SetupFunscriptEvents();

        if (_camera != null) _camera.Current = true;

        if (_videoPath != null) LoadVideo(_videoPath);
        if (_scriptPath != null) _ = LoadScriptAsync(_scriptPath);
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
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                NoDepthTest = true, 
                RenderPriority = 1  
            };
            _uiMesh.MaterialOverride = mat;
        }
    }

    private void SetupUIPointers()
    {
        _rightPointer = GetNodeOrNull<UIPointer>("XROrigin3D/XRController3DRight");
        _leftController = GetNodeOrNull<XRController3D>("XROrigin3D/XRController3DLeft");

        if (_rightPointer != null)
        {
            var laser = _rightPointer.GetNodeOrNull<MeshInstance3D>("LaserMesh");
            if (laser != null)
            {
                var laserMat = new StandardMaterial3D
                {
                    AlbedoColor = new Color(1.0f, 0.0f, 0.0f, 0.6f),
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    ShadingMode = StandardMaterial3D.ShadingModeEnum.Unshaded,
                    NoDepthTest = true, 
                    RenderPriority = 2  
                };
                laser.MaterialOverride = laserMat;
            }
        }
    }

    private void SetupOverlay()
    {
        _overlay = GetNodeOrNull<VrOverlay>("SubViewport/VrOverlay");
        if (_overlay == null) return;

        System.Action returnToMain = () =>
        {
            _videoManager?.Stop();
            _funscriptPlayer?.SetActions(null);

            var mainScenePack = ResourceLoader.Load<PackedScene>("res://Scenes/Main3D.tscn");
            if (mainScenePack != null)
            {
                var mainInstance = mainScenePack.Instantiate();
                var currentScene = GetTree().CurrentScene;
                GetTree().Root.AddChild(mainInstance);
                GetTree().CurrentScene = mainInstance;
                currentScene?.QueueFree();
            }
        };

        _overlay.OnPlayPause += () => _videoManager?.TogglePlayPause();
        _overlay.ResetHideTimer();

        _overlay.OnStop += returnToMain;
        _overlay.OnBack += returnToMain;

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

        _overlay.OnMaxPercentageChange += (value) => SetMaxPercentage(value);
        _overlay.OnIpdChange += (ipdMm) =>
        {
            float uvOffset = (ipdMm - 63.0f) * 0.001f;
            if (_sphereMaterial != null) _sphereMaterial.SetShaderParameter("ipd_offset", uvOffset);
            if (_flatMaterial != null) _flatMaterial.SetShaderParameter("ipd_offset", uvOffset);
            _overlay.ResetHideTimer();
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
        _overlay?.UpdateState(
            _videoManager?.State == PlaybackState.Playing,
            _videoManager?.PositionMs ?? 0,
            _videoManager?.DurationMs ?? 0,
            _playbackSpeed
        );

        RecenterVideo();
        if (_uiMesh != null && _uiMesh.Visible)
        {
            RecenterUI();
        }
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
        var sphere = new SphereMesh { Radius = 50.0f, Height = 100.0f, RadialSegments = 128, Rings = 64 };
        _sphereMeshInstance.Mesh = sphere;
        _sphereMeshInstance.Visible = _currentFormat != VideoFormat.Flat;
    }

    private void SetupFlatMesh()
    {
        if (_flatMeshInstance == null) return;
        var quad = new QuadMesh { Size = new Vector2(16.0f, 9.0f) };
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
                _sphereMaterial = new ShaderMaterial { Shader = sphereShader };
                _sphereMaterial.SetShaderParameter("projection_mode", (int)_currentFormat);
                _sphereMaterial.SetShaderParameter("ipd_offset", 0.0f);
                _sphereMeshInstance.MaterialOverride = _sphereMaterial;
            }
        }

        if (_flatMeshInstance != null)
        {
            var flatShader = ResourceLoader.Load<Shader>("res://Resources/Shaders/video_flat.gdshader");
            if (flatShader != null)
            {
                _flatMaterial = new ShaderMaterial { Shader = flatShader };
                _flatMaterial.SetShaderParameter("ipd_offset", 0.0f);
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
            _isSyncing = _videoManager.State == PlaybackState.Playing && _funscriptPlayer?.Actions is { Count: > 0 };
            if (_isSyncing)
            {
                _funscriptPlayer!.SyncToPosition(posMs);
            }
            _overlay?.UpdateState(_videoManager.State == PlaybackState.Playing, posMs, _videoManager.DurationMs, _playbackSpeed);
        };
        _videoManager.OnDimensionsChanged += (w, h) => AdjustMeshForVideo();
    }

    private void SetupFunscriptEvents()
    {
        if (_funscriptPlayer != null && _outputManager != null)
        {
            _funscriptPlayer.OnOutputPosition += (pos) => _outputManager.SendPosition(pos);
        }
    }

    private void LoadVideo(string path)
    {
        if (_videoManager == null) return;
        _videoManager.LoadFile(path, _currentFormat);
        AdjustMeshForVideo();
        _overlay?.ResetHideTimer();
        if (_videoManager.State != PlaybackState.Error)
        {
            _videoManager.Play();
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
            float baseHeight = 12.0f; 
            float newWidth = baseHeight * ((float)w / h);
            var quad = new QuadMesh { Size = new Vector2(newWidth, baseHeight) };
            _flatMeshInstance.Mesh = quad;
        }
    }

    // --- 重置视图：带 Pitch 角度偏移缓存 ---
    private void RecenterVideo()
    {
        if (_camera == null) return;
        Vector3 camPos = _camera.GlobalPosition;
        Vector3 camForward = (-_camera.GlobalBasis.Z with { Y = 0 }).Normalized();

        if (_flatMeshInstance != null)
        {
            _flatMeshInstance.GlobalPosition = camPos + camForward * 8.0f;
            _flatMeshInstance.LookAt(camPos, Vector3.Up);
            _flatMeshInstance.RotateObjectLocal(Vector3.Up, Mathf.Pi);
            
            _flatBaseBasis = _flatMeshInstance.Basis;
            _flatMeshInstance.Basis = _flatBaseBasis * new Basis(Vector3.Right, _videoPitchOffset);
        }

        if (_sphereMeshInstance != null)
        {
            _sphereMeshInstance.GlobalPosition = camPos;
            _sphereMeshInstance.Rotation = new Vector3(_videoPitchOffset, _sphereMeshInstance.Rotation.Y, _sphereMeshInstance.Rotation.Z);
        }
    }

    private void RecenterUI()
    {
        if (_camera == null || _uiMesh == null) return;
        Vector3 camPos = _camera.GlobalPosition;
        Vector3 camForward = (-_camera.GlobalBasis.Z with { Y = 0 }).Normalized();

        float uiDistance = (_currentFormat == VideoFormat.Flat) ? 1.5f : 2.5f;
        _uiMesh.GlobalPosition = camPos + camForward * uiDistance + new Vector3(0, -0.2f, 0);

        float uiScale = (_currentFormat == VideoFormat.Flat) ? 1.0f : 1.4f;
        _uiMesh.Scale = new Vector3(uiScale, uiScale, uiScale);

        _uiMesh.LookAt(camPos, Vector3.Up);
        _uiMesh.RotateObjectLocal(Vector3.Up, Mathf.Pi);
    }

    // --- 摇杆 + 扳机 控制视频俯仰角 ---
    private void HandleVideoRotation(double delta)
    {
        float inputY = 0f;

        if (_rightPointer != null && _rightPointer.IsButtonPressed("trigger_click"))
        {
            inputY = _rightPointer.GetVector2("primary").Y;
        }
        else if (_leftController != null && _leftController.IsButtonPressed("trigger_click"))
        {
            inputY = _leftController.GetVector2("primary").Y;
        }

        if (Mathf.Abs(inputY) > 0.05f)
        {
            _videoPitchOffset += inputY * 1.5f * (float)delta; 
            float maxPitch = Mathf.DegToRad(80.0f);
            _videoPitchOffset = Mathf.Clamp(_videoPitchOffset, -maxPitch, maxPitch);

            UpdateVideoRotation();
        }
    }

    private void UpdateVideoRotation()
    {
        if (_sphereMeshInstance != null)
        {
            _sphereMeshInstance.Rotation = new Vector3(_videoPitchOffset, _sphereMeshInstance.Rotation.Y, _sphereMeshInstance.Rotation.Z);
        }
        if (_flatMeshInstance != null)
        {
            _flatMeshInstance.Basis = _flatBaseBasis * new Basis(Vector3.Right, _videoPitchOffset);
        }
    }

    public override void _Process(double delta)
    {
        if (_overlay == null) return;

        if (_framesSinceStart < 15) _framesSinceStart++;
        if (_framesSinceStart == 15)
        {
            RecenterVideo();
            RecenterUI();
        }

        bool isMenuPressed = false;
        if (_leftController != null)
        {
            isMenuPressed |= _leftController.IsButtonPressed("menu_button") || 
                             _leftController.IsButtonPressed("ax_button") || 
                             _leftController.IsButtonPressed("by_button");
        }

        if (_rightPointer != null)
        {
            isMenuPressed |= _rightPointer.IsButtonPressed("menu_button") || 
                             _rightPointer.IsButtonPressed("ax_button") || 
                             _rightPointer.IsButtonPressed("by_button");
        }

        if (isMenuPressed && !_wasMenuPressed)
        {
            _overlay.ToggleVisibility();
            RecenterUI();
        }
        _wasMenuPressed = isMenuPressed;

        HandleVideoRotation(delta);
    }

    private async Task LoadScriptAsync(string path)
    {
        var script = await FunscriptFile.FromFileAsync(path);
        if (script != null)
        {
            _funscriptPlayer?.SetActions(script.Actions);
        }
    }

    public override void _ExitTree()
    {
        _outputManager?.Dispose();
    }
}