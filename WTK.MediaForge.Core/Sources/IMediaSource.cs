using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Core.Sources;

internal enum MediaSourceState
{
    Stopped = 0,
    Starting = 1,
    Running = 2,
    Paused = 3,
    Failed = 4,
    Stopping = 5
}

internal interface IMediaSource
{
    SourceId Id { get; }

    string Name { get; }

    MediaSourceState State { get; }

    Exception? LastError { get; }

    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}
