using WTK.MediaForge.Composition;
using WTK.MediaForge.Windows.Media.Ndi;
using Xunit;

namespace WTK.MediaForge.Windows.Tests.Media;

public sealed class WindowsNdiSourceDiscoveryTests
{
    [Fact]
    public async Task Ndi_source_discovery_returns_standard_sdk_sources_without_video_frames()
    {
        var sdk = new FakeNdiStandardSdk(
        [
            new WindowsNdiSourceInfo("Camera A", "ndi://camera-a"),
            new WindowsNdiSourceInfo("Program", null)
        ]);
        var discovery = new WindowsNdiSourceDiscovery(
            new FakeNdiRuntimeProbe(Runtime(supportsDiscovery: true)),
            new FakeNdiStandardSdkFactory(sdk));

        var sources = await discovery.FindSourcesAsync(new WindowsNdiDiscoveryOptions
        {
            DiscoveryTimeout = TimeSpan.FromMilliseconds(5),
            Groups = "public",
            ExtraIps = "192.168.1.10"
        });

        Assert.True(sdk.InitializeCalled);
        Assert.Equal(TimeSpan.FromMilliseconds(5), sdk.OptionsSeen?.DiscoveryTimeout);
        Assert.Equal("public", sdk.OptionsSeen?.Groups);
        Assert.Equal("192.168.1.10", sdk.OptionsSeen?.ExtraIps);
        Assert.Collection(
            sources,
            source => Assert.Equal("Camera A", source.Name),
            source => Assert.Equal("Program", source.Name));
    }

    [Fact]
    public async Task Ndi_source_discovery_rejects_runtime_without_discovery_exports()
    {
        var discovery = new WindowsNdiSourceDiscovery(
            new FakeNdiRuntimeProbe(Runtime(supportsDiscovery: false)),
            new FakeNdiStandardSdkFactory(new FakeNdiStandardSdk([])));

        var ex = await Assert.ThrowsAsync<MediaForgeUnsupportedFeatureException>(async () =>
            await discovery.FindSourcesAsync());

        Assert.Contains("does not expose", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Ndi_source_discovery_rejects_missing_runtime_with_diagnostic()
    {
        var discovery = new WindowsNdiSourceDiscovery(
            new FakeNdiRuntimeProbe(new WindowsNdiRuntimeInfo(
                IsRuntimePresent: false,
                IsLoadable: false,
                LibraryPath: null,
                Version: null,
                SupportsStandardSourceDiscovery: false,
                Reason: "Missing NDI runtime.")),
            new FakeNdiStandardSdkFactory(new FakeNdiStandardSdk([])));

        var ex = await Assert.ThrowsAsync<MediaForgeUnsupportedFeatureException>(async () =>
            await discovery.FindSourcesAsync());

        Assert.Contains("Missing NDI runtime", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Ndi_source_discovery_rejects_negative_timeout()
    {
        var discovery = new WindowsNdiSourceDiscovery(
            new FakeNdiRuntimeProbe(Runtime(supportsDiscovery: true)),
            new FakeNdiStandardSdkFactory(new FakeNdiStandardSdk([])));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await discovery.FindSourcesAsync(new WindowsNdiDiscoveryOptions
            {
                DiscoveryTimeout = TimeSpan.FromMilliseconds(-1)
            }));
    }

    private static WindowsNdiRuntimeInfo Runtime(bool supportsDiscovery) =>
        new(
            IsRuntimePresent: true,
            IsLoadable: true,
            LibraryPath: @"C:\NDI\Processing.NDI.Lib.x64.dll",
            Version: "NDI test runtime",
            SupportsStandardSourceDiscovery: supportsDiscovery,
            Reason: "Detected.");

    private sealed class FakeNdiRuntimeProbe(WindowsNdiRuntimeInfo info) : IWindowsNdiRuntimeProbe
    {
        public WindowsNdiRuntimeInfo Probe() => info;
    }

    private sealed class FakeNdiStandardSdkFactory(FakeNdiStandardSdk sdk) : IWindowsNdiStandardSdkFactory
    {
        public IWindowsNdiStandardSdk Load(string libraryPath)
        {
            Assert.Equal(@"C:\NDI\Processing.NDI.Lib.x64.dll", libraryPath);
            return sdk;
        }
    }

    private sealed class FakeNdiStandardSdk(IReadOnlyList<WindowsNdiSourceInfo> sources) : IWindowsNdiStandardSdk
    {
        public bool InitializeCalled { get; private set; }

        public WindowsNdiDiscoveryOptions? OptionsSeen { get; private set; }

        public bool Initialize()
        {
            InitializeCalled = true;
            return true;
        }

        public IReadOnlyList<WindowsNdiSourceInfo> FindSources(
            WindowsNdiDiscoveryOptions options,
            CancellationToken cancellationToken)
        {
            OptionsSeen = options;
            cancellationToken.ThrowIfCancellationRequested();
            return sources;
        }

        public void Dispose()
        {
        }
    }
}
