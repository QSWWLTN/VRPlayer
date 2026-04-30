using Godot;

namespace VRPlayerProject.Services;

public partial class HeadTrackerService : Node
{
    private bool _enabled = true;
    private Vector3 _rotationEuler;
    private float _rotationSmooth;

    public event Action<Vector3>? OnRotationChanged;

    public void SetEnabled(bool enabled) => _enabled = enabled;
    public void ResetOrientation() => _rotationEuler = Vector3.Zero;

    public override void _Input(InputEvent @event)
    {
        if (!_enabled) return;

        if (@event is InputEventMouseMotion motion)
        {
            _rotationEuler.X -= Mathf.DegToRad(motion.Relative.Y * 0.15f);
            _rotationEuler.Y -= Mathf.DegToRad(motion.Relative.X * 0.15f);
            _rotationEuler.X = Mathf.Clamp(_rotationEuler.X, Mathf.DegToRad(-89f), Mathf.DegToRad(89f));

            OnRotationChanged?.Invoke(_rotationEuler);
        }
    }

    public override void _Process(double delta)
    {
        if (!_enabled) return;

        var camera = GetViewport().GetCamera3D();
        if (camera != null)
        {
            camera.Rotation = _rotationEuler;
        }
    }
}
