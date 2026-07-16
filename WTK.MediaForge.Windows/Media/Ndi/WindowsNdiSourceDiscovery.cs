using WTK.MediaForge.Composition;

namespace WTK.MediaForge.Windows.Media.Ndi;

public sealed record WindowsNdiSourceInfo(
    string Name,
    string? UrlAddress);

public sealed class WindowsNdiDiscoveryOptions
{
    public static WindowsNdiDiscoveryOptions Default { get; } = new();

    public bool ShowLocalSources { get; init; } = true;

    public string? Groups { get; init; }

    public string? ExtraIps { get; init; }

    public TimeSpan DiscoveryTimeout { get; init; } = TimeSpan.FromSeconds(2);
}

internal interface IWindowsNdiSourceDiscovery
{
    ValueTask<IReadOnlyList<WindowsNdiSourceInfo>> FindSourcesAsync(
        WindowsNdiDiscoveryOptions? options = null,
        CancellationToken cancellationToken = default);
}

internal sealed class WindowsNdiSourceDiscovery(
    IWindowsNdiRuntimeProbe? runtimeProbe = null,
    IWindowsNdiStandardSdkFactory? sdkFactory = null) : IWindowsNdiSourceDiscovery
{
    private readonly IWindowsNdiRuntimeProbe _runtimeProbe = runtimeProbe ?? new WindowsNdiRuntimeProbe();
    private readonly IWindowsNdiStandardSdkFactory _sdkFactory = sdkFactory ?? new WindowsNdiStandardSdkFactory();

    public async ValueTask<IReadOnlyList<WindowsNdiSourceInfo>> FindSourcesAsync(
        WindowsNdiDiscoveryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= WindowsNdiDiscoveryOptions.Default;
        ValidateOptions(options);

        var runtime = _runtimeProbe.Probe();
        if (!runtime.CanUseStandardSdk)
            throw CreateUnsupported(runtime.Reason);

        if (!runtime.SupportsStandardSourceDiscovery)
        {
            throw CreateUnsupported(
                $"NDI runtime is loadable at '{runtime.LibraryPath}', but it does not expose the Standard SDK source discovery entry points.");
        }

        return await Task.Run(
            () => FindSources(runtime, options, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    private IReadOnlyList<WindowsNdiSourceInfo> FindSources(
        WindowsNdiRuntimeInfo runtime,
        WindowsNdiDiscoveryOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var sdk = _sdkFactory.Load(runtime.LibraryPath!);
        if (!sdk.Initialize())
            throw CreateUnsupported("NDI Standard SDK initialization failed.");

        return sdk.FindSources(options, cancellationToken);
    }

    private static void ValidateOptions(WindowsNdiDiscoveryOptions options)
    {
        if (options.DiscoveryTimeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "NDI discovery timeout cannot be negative.");

        if (options.DiscoveryTimeout.TotalMilliseconds > uint.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(options), "NDI discovery timeout is too large.");
    }

    private static MediaForgeUnsupportedFeatureException CreateUnsupported(string reason) =>
        new(
            "source.wtk.source.ndi.input.discovery",
            $"NDI source discovery is unavailable. {reason}");
}
