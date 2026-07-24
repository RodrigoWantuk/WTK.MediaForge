using WTK.MediaForge.Remote;
using Xunit;

namespace WTK.MediaForge.Remote.Tests;

public sealed class RemoteSceneCoordinatorTests
{
    [Fact]
    public async Task Coordinator_disposes_session_then_transport_once()
    {
        var session = new FakeSession();
        var transport = new FakeTransport(session);
        var coordinator = new RemoteSceneCoordinator(transport);
        await coordinator.ConnectAsync(Request(), CancellationToken.None);

        await coordinator.DisposeAsync();
        await coordinator.DisposeAsync();

        Assert.Equal(1, session.DisposeCount);
        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task Coordinator_reports_all_finalization_failures()
    {
        var coordinator = new RemoteSceneCoordinator(new FakeTransport(new FakeSession(failDispose: true), failDispose: true));
        await coordinator.ConnectAsync(Request(), CancellationToken.None);

        var error = await Assert.ThrowsAsync<AggregateException>(() => coordinator.DisposeAsync().AsTask());

        Assert.Equal(2, error.InnerExceptions.Count);
    }

    private static RemoteSceneConnectionRequest Request() => new(
        new WebRtcConnectionOptions { SignalingServer = new Uri("wss://signal.invalid") },
        new RemoteSceneRuntimeCredentials());

    private sealed class FakeTransport(FakeSession session, bool failDispose = false) : IRemoteSceneTransport
    {
        public int DisposeCount { get; private set; }
        public Task<IRemoteSceneSession> ConnectAsync(RemoteSceneConnectionRequest request, CancellationToken cancellationToken) => Task.FromResult<IRemoteSceneSession>(session);
        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return failDispose ? ValueTask.FromException(new InvalidOperationException("transport dispose")) : ValueTask.CompletedTask;
        }
    }

    private sealed class FakeSession(bool failDispose = false) : IRemoteSceneSession
    {
        public int DisposeCount { get; private set; }
        public RemoteSceneConnectionState State => RemoteSceneConnectionState.ConnectedDirect;
        public RemoteSceneTelemetry Telemetry => new(State, 0, null, 0, 0, false);
        public Task<IRemoteScenePublisher> PublishAsync(RemoteScenePublishRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IRemoteSceneSubscriber> SubscribeAsync(RemoteSceneSubscribeRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return failDispose ? ValueTask.FromException(new InvalidOperationException("session dispose")) : ValueTask.CompletedTask;
        }
    }
}
