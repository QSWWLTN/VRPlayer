using Godot;

namespace VRPlayerProject.Rendering;

public class LensDistortion
{
    public double DistortionK1 { get; set; } = -0.55;
    public double DistortionK2 { get; set; } = 0.22;
    public double IpdMm { get; set; } = 63.0;

    public Vector2 Distort(Vector2 uv, double screenAspect, bool isLeftEye)
    {
        double centerX = isLeftEye ? 0.25 : 0.75;
        double dx = (uv.X - centerX) * screenAspect;
        double dy = uv.Y - 0.5;
        double r2 = dx * dx + dy * dy;
        double factor = 1.0 + DistortionK1 * r2 + DistortionK2 * r2 * r2;

        return new Vector2(
            (float)(centerX + dx / factor / screenAspect),
            (float)(0.5 + dy / factor)
        );
    }

    public Vector2 Undistort(Vector2 uv, double screenAspect, bool isLeftEye)
    {
        double centerX = isLeftEye ? 0.25 : 0.75;
        double dx = (uv.X - centerX) * screenAspect;
        double dy = uv.Y - 0.5;
        double r2 = dx * dx + dy * dy;
        double factor = 1.0 + DistortionK1 * r2 + DistortionK2 * r2 * r2;

        return new Vector2(
            (float)(centerX + dx * factor / screenAspect),
            (float)(0.5 + dy * factor)
        );
    }

    public bool IsInLensArea(Vector2 uv, bool isLeftEye)
    {
        double centerX = isLeftEye ? 0.25 : 0.75;
        double dx = uv.X - centerX;
        double dy = uv.Y - 0.5;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        return dist < 0.35;
    }
}
