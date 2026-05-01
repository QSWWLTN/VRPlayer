namespace VRPlayerProject;

public enum VideoFormat
{
	Flat = 0,
	Mono180 = 1,
	Fisheye180 = 2,
	Mono360 = 3,
	Stereo360 = 4,
	Stereo180 = 5  // 新增 180° 3D 格式
}

public enum PlaybackState
{
	Idle,
	Loading,
	Playing,
	Paused,
	Ended,
	Error
}

public enum ProtocolType
{
	TCode,
	Raw
}

public enum SyncState
{
	Idle,
	Syncing,
	InSync,
	Paused,
	Finished,
	Error
}
