using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Studio.DesignData;
using WTK.MediaForge.Studio.Engine;
using WTK.MediaForge.Studio.Services;
using Xunit;

namespace WTK.MediaForge.Studio.Tests;

public sealed class RuntimeStudioProjectServiceTests
{
    [Fact]
    public async Task Save_and_open_roundtrip_uses_canonical_engine_project()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wtk-mediaforge-studio-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "project.mforge.json");
        try
        {
            var mapper = new StudioProjectEngineMapper();
            var service = new RuntimeStudioProjectService(mapper, path);
            var original = StudioMockDocumentFactory.Create();

            await service.SaveAsync(original, path, CancellationToken.None);
            var json = await File.ReadAllTextAsync(path);
            var canonical = MediaForgeProjectSerializer.Deserialize(json);
            var restored = await service.OpenAsync(path, CancellationToken.None);

            Assert.Equal(original.Scenes.Count, canonical.Canvases.Count);
            Assert.Equal(original.Sources.Count(source => source.TypeId is not ("source.text" or "source.solid")), canonical.SourceDefinitions.Count);
            Assert.Equal(original.Scenes.Count, restored.Scenes.Count);
            Assert.Equal(original.Outputs.Count, restored.Outputs.Count);
            Assert.False(restored.HasUnsavedChanges);
            Assert.Equal(Path.GetFullPath(path), service.Current.Path);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Open_rejects_invalid_project_without_changing_current_document()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wtk-mediaforge-studio-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "invalid.mforge.json");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, "{\"schemaVersion\":999}");
        try
        {
            var service = new RuntimeStudioProjectService(new StudioProjectEngineMapper(), path);
            var previousName = service.Current.DisplayName;

            await Assert.ThrowsAsync<InvalidDataException>(() => service.OpenAsync(path, CancellationToken.None));

            Assert.Equal(previousName, service.Current.DisplayName);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
