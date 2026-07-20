using System.Globalization;
using System.Text;
using WTK.MediaForge.Studio.DocumentModel;

namespace WTK.MediaForge.Studio.Services;

public interface IStudioUndoRedoService
{
    bool CanUndo { get; }

    bool CanRedo { get; }

    bool IsCurrentClean { get; }

    void Reset(StudioScene scene);

    void Record(StudioScene scene);

    StudioScene Undo();

    StudioScene Redo();
}

public sealed class StudioUndoRedoService : IStudioUndoRedoService
{
    private const int MaxSnapshots = 100;
    private readonly List<StudioScene> _snapshots = [];
    private readonly List<string> _fingerprints = [];
    private string _cleanFingerprint = string.Empty;
    private int _index = -1;

    public bool CanUndo => _index > 0;

    public bool CanRedo => _index >= 0 && _index < _snapshots.Count - 1;

    public bool IsCurrentClean => _index >= 0 && _fingerprints[_index] == _cleanFingerprint;

    public void Reset(StudioScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        _snapshots.Clear();
        _fingerprints.Clear();
        var clone = SceneEditSessionService.CloneScene(scene);
        _snapshots.Add(clone);
        _cleanFingerprint = Fingerprint(clone);
        _fingerprints.Add(_cleanFingerprint);
        _index = 0;
    }

    public void Record(StudioScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        if (_index < 0)
        {
            Reset(scene);
            return;
        }

        var fingerprint = Fingerprint(scene);
        if (_fingerprints[_index] == fingerprint)
        {
            return;
        }

        if (CanRedo)
        {
            var removeIndex = _index + 1;
            var removeCount = _snapshots.Count - removeIndex;
            _snapshots.RemoveRange(removeIndex, removeCount);
            _fingerprints.RemoveRange(removeIndex, removeCount);
        }

        _snapshots.Add(SceneEditSessionService.CloneScene(scene));
        _fingerprints.Add(fingerprint);
        _index = _snapshots.Count - 1;

        if (_snapshots.Count <= MaxSnapshots)
        {
            return;
        }

        _snapshots.RemoveAt(1);
        _fingerprints.RemoveAt(1);
        if (_index > 1)
        {
            _index--;
        }
    }

    public StudioScene Undo()
    {
        if (!CanUndo)
        {
            throw new InvalidOperationException("There is no scene edit to undo.");
        }

        _index--;
        return SceneEditSessionService.CloneScene(_snapshots[_index]);
    }

    public StudioScene Redo()
    {
        if (!CanRedo)
        {
            throw new InvalidOperationException("There is no scene edit to redo.");
        }

        _index++;
        return SceneEditSessionService.CloneScene(_snapshots[_index]);
    }

    private static string Fingerprint(StudioScene scene)
    {
        var builder = new StringBuilder();
        Append(builder, scene.Id);
        Append(builder, scene.DisplayName);
        Append(builder, scene.Metadata);
        Append(builder, scene.IsProgram);
        Append(builder, scene.Canvas.Width);
        Append(builder, scene.Canvas.Height);
        Append(builder, scene.Canvas.FrameRate);
        Append(builder, scene.Canvas.BackgroundColor);

        foreach (var outputId in scene.OutputIds.Order(StringComparer.Ordinal))
        {
            Append(builder, outputId);
        }

        foreach (var effect in scene.Effects.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            AppendEffect(builder, effect);
        }

        foreach (var layer in scene.Layers.OrderBy(item => item.Order).ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            Append(builder, layer.Id);
            Append(builder, layer.Name);
            Append(builder, layer.SourceId);
            Append(builder, layer.SourceName);
            Append(builder, layer.Type);
            Append(builder, layer.Order);
            Append(builder, layer.IsVisible);
            Append(builder, layer.IsLocked);
            Append(builder, layer.BlendMode);
            Append(builder, layer.Crop.Left);
            Append(builder, layer.Crop.Top);
            Append(builder, layer.Crop.Right);
            Append(builder, layer.Crop.Bottom);
            Append(builder, layer.Transform.X);
            Append(builder, layer.Transform.Y);
            Append(builder, layer.Transform.Width);
            Append(builder, layer.Transform.Height);
            Append(builder, layer.Transform.RotationDegrees);
            Append(builder, layer.Transform.Opacity);

            foreach (var effect in layer.Effects.OrderBy(item => item.Id, StringComparer.Ordinal))
            {
                AppendEffect(builder, effect);
            }
        }

        return builder.ToString();
    }

    private static void AppendEffect(StringBuilder builder, StudioEffect effect)
    {
        Append(builder, effect.Id);
        Append(builder, effect.Name);
        Append(builder, effect.Description);
        Append(builder, effect.IsEnabled);
        Append(builder, effect.IsExpanded);
        Append(builder, effect.KeyColor);
        Append(builder, effect.Tolerance);
        Append(builder, effect.Spill);
        Append(builder, effect.EdgeSmooth);
    }

    private static void Append<T>(StringBuilder builder, T value)
    {
        builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
        builder.Append('\u001f');
    }
}
