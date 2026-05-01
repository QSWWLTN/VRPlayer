using Godot;

namespace VRPlayerProject.UI;

public partial class VrPanel3D : Node3D
{
	private SubViewport _subViewport = null!;
	private MeshInstance3D _panelMesh = null!;
	private StaticBody3D _staticBody = null!;
	private CollisionShape3D _collisionShape = null!;
	private QuadMesh _quadMesh = null!;
	private ShaderMaterial _panelMaterial = null!;

    private Vector2 _panelWorldSize = new(3.2f, 1.8f);
    private Vector2I _viewportResolution = new(1280, 720);
    private float _distanceFromCamera = 4.0f;
    private bool _followCamera = true;
    private bool _billboard = true;

    private double _lastClickTimeMs;

	public SubViewport Viewport => _subViewport;
	public MeshInstance3D PanelMesh => _panelMesh;

	[Export]
	public Vector2 PanelWorldSize
	{
		get => _panelWorldSize;
		set { _panelWorldSize = value; ApplySize(); }
	}

	[Export]
	public Vector2I ViewportResolution
	{
		get => _viewportResolution;
		set { _viewportResolution = value; if (_subViewport != null) _subViewport.Size = value; }
	}

	[Export]
	public float DistanceFromCamera
	{
		get => _distanceFromCamera;
		set => _distanceFromCamera = value;
	}

	[Export]
	public bool FollowCamera
	{
		get => _followCamera;
		set => _followCamera = value;
	}

	[Export]
	public bool Billboard
	{
		get => _billboard;
		set => _billboard = value;
	}

	[Export]
	public PackedScene? ContentScene { get; set; }

	public override void _Ready()
	{
		InitializeXR();
		CreatePanel();
		EnsureWorldEnvironment();

		if (ContentScene != null)
		{
			var content = ContentScene.Instantiate<Control>();
			SetContent(content);
		}
	}

	private void InitializeXR()
	{
		var xrInterface = XRServer.FindInterface("OpenXR");
		if (xrInterface != null)
		{
			if (!xrInterface.IsInitialized())
			{
				xrInterface.Initialize();
			}
			if (xrInterface.IsInitialized())
			{
				GetViewport().UseXR = true;
				GD.Print("[VrPanel3D] OpenXR initialized successfully");
			}
			else
			{
				GD.PrintErr("[VrPanel3D] OpenXR initialization failed");
			}
		}
		else
		{
			GD.PrintErr("[VrPanel3D] OpenXR interface not found");
		}
	}

	private void EnsureWorldEnvironment()
	{
		if (GetViewport().GetWorld3D()?.Environment != null)
			return;

		var env = new Godot.Environment();
		env.BackgroundMode = Godot.Environment.BGMode.Color;
		env.BackgroundColor = new Color(0, 0, 0, 1);
		var worldEnv = new WorldEnvironment { Environment = env };
		AddChild(worldEnv);
	}

	private void CreatePanel()
	{
		_subViewport = new SubViewport
		{
			Size = _viewportResolution,
			TransparentBg = true,
			OwnWorld3D = false,
			Disable3D = true,
			HandleInputLocally = false
		};
		AddChild(_subViewport);

		_quadMesh = new QuadMesh();
		_quadMesh.Size = _panelWorldSize;

		_panelMaterial = new ShaderMaterial();
		var shader = new Shader();
		shader.Code = @"shader_type spatial;
render_mode unshaded, cull_disabled, blend_mix, depth_draw_never;

uniform sampler2D viewport_texture : source_color, filter_linear_mipmap;
uniform float alpha_cutoff = 0.01;

void fragment() {
    vec4 c = texture(viewport_texture, UV);
    ALBEDO = c.rgb;
    ALPHA = c.a;
    if (c.a < alpha_cutoff) discard;
}";
		_panelMaterial.Shader = shader;
		_panelMaterial.SetShaderParameter("viewport_texture", _subViewport.GetTexture());
		_panelMaterial.RenderPriority = 10;

		_panelMesh = new MeshInstance3D
		{
			Mesh = _quadMesh,
			MaterialOverride = _panelMaterial,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
		};
		AddChild(_panelMesh);

		_staticBody = new StaticBody3D();
		_collisionShape = new CollisionShape3D();
		var boxShape = new BoxShape3D();
		boxShape.Size = new Vector3(_panelWorldSize.X, _panelWorldSize.Y, 0.1f);
		_collisionShape.Shape = boxShape;
		_staticBody.AddChild(_collisionShape);
		_staticBody.InputEvent += OnPanelInputEvent;
		_staticBody.InputRayPickable = true;
		AddChild(_staticBody);
	}

	public void SetContent(Control control)
	{
		foreach (var child in _subViewport.GetChildren())
			child.QueueFree();

		var wrapper = new Control
		{
			LayoutMode = 1,
			AnchorRight = 1.0f,
			AnchorBottom = 1.0f,
			MouseFilter = Control.MouseFilterEnum.Stop
		};
		_subViewport.AddChild(wrapper);

		control.LayoutMode = 1;
		control.AnchorRight = 1.0f;
		control.AnchorBottom = 1.0f;
		wrapper.AddChild(control);
	}

	private void ApplySize()
	{
		_quadMesh.Size = _panelWorldSize;
		if (_collisionShape?.Shape is BoxShape3D box)
			box.Size = new Vector3(_panelWorldSize.X, _panelWorldSize.Y, 0.1f);
	}

	public void SetInteractable(bool interactable)
	{
		_staticBody.InputRayPickable = interactable;
	}

	public override void _Process(double delta)
	{
		if (!_followCamera) return;

		var camera = GetViewport().GetCamera3D();
		if (camera == null) return;

		Vector3 camPos = camera.GlobalPosition;
		Vector3 camForward = -camera.GlobalBasis.Z;

		GlobalPosition = camPos + camForward * _distanceFromCamera;

		if (_billboard)
		{
			Vector3 dirToCamera = (camPos - GlobalPosition).Normalized();
			if (dirToCamera.LengthSquared() > 0.0001f)
			{
				var up = Vector3.Up;
				var right = up.Cross(-dirToCamera).Normalized();
				if (right.LengthSquared() < 0.0001f)
					right = Vector3.Right;
				var adjustedUp = (-dirToCamera).Cross(right).Normalized();

				GlobalBasis = new Basis(right, adjustedUp, -dirToCamera);
			}
		}
	}

    private void OnPanelInputEvent(Node camera, InputEvent @event, Vector3 position, Vector3 normal, long shapeIdx)
    {
        if (@event is InputEventMouseButton mouseButton)
        {
            var viewportPos = WorldToPanelUV(position);

            bool isDoubleClick = false;
            if (mouseButton.Pressed)
            {
                double now = Time.GetTicksMsec();
                if (now - _lastClickTimeMs < 350.0)
                    isDoubleClick = true;
                _lastClickTimeMs = now;
            }

            var forwarded = new InputEventMouseButton
            {
                ButtonIndex = mouseButton.ButtonIndex,
                Pressed = mouseButton.Pressed,
                Position = viewportPos,
                DoubleClick = isDoubleClick
            };
            _subViewport.PushInput(forwarded);
        }
        else if (@event is InputEventMouseMotion mouseMotion)
        {
            var viewportPos = WorldToPanelUV(position);
            var forwarded = new InputEventMouseMotion
            {
                Position = viewportPos
            };
            _subViewport.PushInput(forwarded);
        }
    }

	private Vector2 WorldToPanelUV(Vector3 worldHit)
	{
		var localHit = GlobalTransform.AffineInverse() * worldHit;
		float u = (localHit.X / _panelWorldSize.X) + 0.5f;
		float v = (-localHit.Y / _panelWorldSize.Y) + 0.5f;
		return new Vector2(u * _viewportResolution.X, v * _viewportResolution.Y);
	}
}
