using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Core.Sources;

public enum MediaSourceState
{
    Stopped = 0,
    Starting = 1,
    Running = 2,
    Failed = 3,
    Stopping = 4
}

public interface IMediaSource
{
    SourceId Id { get; }

    string Name { get; }

    MediaSourceState State { get; }

    Exception? LastError { get; }

    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}
