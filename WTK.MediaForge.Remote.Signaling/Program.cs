namespace WTK.MediaForge.Remote.Signaling;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var app = RemoteSceneSignalingHost.Build(args);
        await app.RunAsync().ConfigureAwait(false);
    }
}
