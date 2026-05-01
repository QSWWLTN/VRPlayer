namespace VRPlayerProject.Models;

public class VideoFile
{
	public string Path { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public VideoFormat Format { get; set; } = VideoFormat.Mono360;
	public double DurationMs { get; set; }
}

public class VrSettings
{
	public VideoFormat VideoFormat { get; set; } = VideoFormat.Mono360;
	public bool HeadTrackingEnabled { get; set; } = true;
	public double Ipd { get; set; } = 63.0;
}
