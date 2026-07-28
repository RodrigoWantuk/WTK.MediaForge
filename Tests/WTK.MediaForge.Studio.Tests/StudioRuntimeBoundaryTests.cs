using WTK.MediaForge.Composition.Runtime;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Studio.Models;
using WTK.MediaForge.Studio.Services;
using Xunit;

namespace WTK.MediaForge.Studio.Tests;

public sealed class StudioRuntimeBoundaryTests
{
    [Fact]
    public async Task Unavailable_platform_runtime_keeps_studio_operable_with_explicit_diagnostics()
    {
        await using var session = await StudioBootstrapper.CreateRuntimeSessionAsync(new UnavailableRuntimeFactory());

        await session.InitializeAsync();

        Assert.Equal(StudioEngineUiState.Failed, session.Shell.EngineStatus.State);
        Assert.False(session.Shell.ToggleStreamingCommand.CanExecute(null));
    }

    [Fact]
    public void Portable_studio_project_has_no_windows_adapter_reference()
    {
        var project = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "WTK.MediaForge.Studio", "WTK.MediaForge.Studio.csproj"));
        Assert.DoesNotContain("WTK.MediaForge.Windows", project, StringComparison.Ordinal);
        Assert.DoesNotContain("-windows", project, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class UnavailableRuntimeFactory : IMediaForgeRuntimeFactory
    {
        public ValueTask<MediaForgeRuntime> CreateAsync(RuntimeCreationRequest request, CancellationToken cancellationToken = default)
        {
            var probe = new NullHardwareMediaCapabilityProbe();
            var snapshot = new MediaForgeCapabilitySnapshot
            {
                Generation = 0,
                CapturedAt = DateTimeOffset.UtcNow,
                Adapter = new MediaForgeHardwareAdapterInfo
                {
                    Platform = "Test",
                    AdapterId = "unavailable",
                    DeviceName = "No adapter",
                    DeviceGeneration = 0
                },
                Report = MediaForgeCapabilityReportBuilder.Build(new HardwareMediaCapabilityReport
                {
                    Platform = "Test",
                    ExportProofStatus = GpuExportProofStatus.Pending,
                    ExportProofReason = "No platform adapter is available."
                })
            };
            return ValueTask.FromResult(MediaForgeRuntime.Unavailable(
                "No platform backend is available.", probe, MediaForgeRuntimeAdapterCatalog.Known,
                _ => ValueTask.FromResult(snapshot)));
        }
    }

    private static string FindRepositoryRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "WTK.MediaForge.sln")))
                return current;
            current = Directory.GetParent(current)?.FullName ?? string.Empty;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
