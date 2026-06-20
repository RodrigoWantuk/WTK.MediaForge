using WTK.MediaForge.Composition.Editor;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Runtime.Outputs;
using WTK.MediaForge.Composition.Runtime.Sources;
using WTK.MediaForge.Composition.Validation;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Engine;

public sealed class MediaForgeEngine : IAsyncDisposable
{
    private readonly IMediaSourceProviderFactory _sourceProviderFactory;
    private readonly IRenderOutputSinkFactory _outputSinkFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public MediaForgeEngine()
        : this(new UnsupportedMediaSourceProviderFactory(), new UnsupportedRenderOutputSinkFactory())
    {
    }

    public MediaForgeEngine(
        IMediaSourceProviderFactory sourceProviderFactory,
        IRenderOutputSinkFactory outputSinkFactory)
    {
        _sourceProviderFactory = sourceProviderFactory ?? throw new ArgumentNullException(nameof(sourceProviderFactory));
        _outputSinkFactory = outputSinkFactory ?? throw new ArgumentNullException(nameof(outputSinkFactory));
    }

    public MediaForgeProject CurrentProject { get; private set; } = new();

    public bool IsRunning { get; private set; }

    public async Task LoadProjectAsync(MediaForgeProject project, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        cancellationToken.ThrowIfCancellationRequested();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var migrateResult = MediaForgeProjectMigrator.Migrate(project);
            if (!migrateResult.Success)
                migrateResult.Validation.ThrowIfInvalid();

            var validation = MediaForgeProjectValidator.Validate(migrateResult.Project!);
            validation.ThrowIfInvalid();

            CurrentProject = migrateResult.Project!;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsRunning = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsRunning = false;
        return Task.CompletedTask;
    }

    public async Task ApplyProjectUpdateAsync(
        Action<MediaForgeProjectEditor> edit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        cancellationToken.ThrowIfCancellationRequested();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var editor = new MediaForgeProjectEditor(CurrentProject);
            edit(editor);
            editor.ValidateOrThrow();
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task BindOutputAsync(
        RenderOutputId outputId,
        RenderOutputTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        var output = CurrentProject.Outputs.FirstOrDefault(o => o.Id == outputId)
            ?? throw new InvalidOperationException($"Output {outputId} was not found in the current project.");

        if (output.TypeId != target.TypeId)
        {
            throw new InvalidOperationException(
                $"Output type '{output.TypeId.Value}' does not match target type '{target.TypeId.Value}'.");
        }

        if (!_outputSinkFactory.CanCreate(target.TypeId))
        {
            throw new NotSupportedException(
                $"No output sink factory registered for type '{target.TypeId.Value}'.");
        }

        _outputSinkFactory.CreateSink(target);
        return Task.CompletedTask;
    }

    public Task UnbindOutputAsync(RenderOutputId outputId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = outputId;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }
}
