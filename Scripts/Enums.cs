namespace VRPlayerProject;

public enum VideoFormat
{
    Flat,
    Mono180,
    Fisheye180,
    Mono360,
    Stereo360,
    Stereo180
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
