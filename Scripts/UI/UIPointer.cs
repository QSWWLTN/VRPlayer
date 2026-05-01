using Godot;

namespace VRPlayerProject.UI;

public partial class UIPointer : XRController3D
{
	private SubViewport? _targetViewport;
	private MeshInstance3D? _targetMesh;
	private RayCast3D? _pointerRay;
	private MeshInstance3D? _laserMesh;

	private bool _isDragging = false;
	private Vector2 _lastMousePos;

	// --- 新增：用于检测双击的计时器变量 ---
	private ulong _lastClickTime = 0;
	private const ulong DoubleClickThresholdMs = 350; // 350毫秒内连按两次算作双击

	public override void _Ready()
	{
		_targetViewport = GetNodeOrNull<SubViewport>("../../SubViewport");
		_targetMesh = GetNodeOrNull<MeshInstance3D>("../../UIMesh");
		_pointerRay = GetNodeOrNull<RayCast3D>("PointerRay");
		_laserMesh = GetNodeOrNull<MeshInstance3D>("LaserMesh");
		
		Tracker = "right_hand";
	}

	public override void _Process(double delta)
	{
		if (_targetViewport == null || _targetMesh == null || _pointerRay == null) return;

		bool isHit = _pointerRay.IsColliding();
		
		if (_laserMesh != null)
		{
			float length = isHit ? GlobalPosition.DistanceTo(_pointerRay.GetCollisionPoint()) : 10.0f;
			_laserMesh.Position = new Vector3(0, 0, -length / 2);
			_laserMesh.Scale = new Vector3(1, 1, length);
		}

		if (!isHit)
		{
			if (_isDragging) 
			{
				SendMouseButton(MouseButton.Left, false, _lastMousePos);
				_isDragging = false;
			}
			return;
		}

		var collider = _pointerRay.GetCollider() as Node3D;
		if (collider == null || collider.GetParent() != _targetMesh) return;

		Vector3 localPoint = _targetMesh.ToLocal(_pointerRay.GetCollisionPoint());
		
		float u = (localPoint.X + 0.8f) / 1.6f;
		float v = (0.6f - localPoint.Y) / 1.2f;
		
		Vector2 mousePos = new Vector2(u * _targetViewport.Size.X, v * _targetViewport.Size.Y);

		if (mousePos != _lastMousePos)
		{
			var motion = new InputEventMouseMotion
			{
				Position = mousePos,
				GlobalPosition = mousePos,
				ButtonMask = _isDragging ? MouseButtonMask.Left : 0
			};
			_targetViewport.PushInput(motion);
			_lastMousePos = mousePos;
		}

		// --- 核心修改：触发器按下的处理逻辑 ---
		bool isTriggerPressed = IsButtonPressed("trigger_click");
		
		if (isTriggerPressed && !_isDragging)
		{
			_isDragging = true;

			// 计算与上一次点击的时间差
			ulong currentTime = Time.GetTicksMsec();
			bool isDoubleClick = (currentTime - _lastClickTime) < DoubleClickThresholdMs;
			_lastClickTime = currentTime;

			// 发送点击事件，并带上是否为双击的标记
			SendMouseButton(MouseButton.Left, true, mousePos, isDoubleClick);
		}
		else if (!isTriggerPressed && _isDragging)
		{
			_isDragging = false;
			SendMouseButton(MouseButton.Left, false, mousePos);
		}
	}

	// --- 修改：增加 doubleClick 参数并传递给系统 ---
	private void SendMouseButton(MouseButton button, bool pressed, Vector2 pos, bool doubleClick = false)
	{
		var btnEvent = new InputEventMouseButton
		{
			ButtonIndex = button,
			Pressed = pressed,
			Position = pos,
			GlobalPosition = pos,
			DoubleClick = doubleClick // 关键：告诉 FileDialog 这是一次双击
		};
		_targetViewport?.PushInput(btnEvent);
	}
}
