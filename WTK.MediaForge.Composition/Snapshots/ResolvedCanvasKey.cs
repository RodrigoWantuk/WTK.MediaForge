using System.Security.Cryptography;
using System.Text;
using WTK.MediaForge.Composition.Scenes.Editing;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Snapshots;

internal readonly record struct ResolvedCanvasKey(
    CanvasId CanvasId,
    SceneVersionId? VersionId,
    SceneVersionBindingKind BindingKind,
    SceneEditSessionId? DraftSessionId,
    string NestedGraphHash)
{
    public bool IsEmpty => CanvasId.IsEmpty || string.IsNullOrWhiteSpace(NestedGraphHash);

    public string StableValue =>
        $"{CanvasId.Value:N}:{VersionId?.Value.ToString("N") ?? "unversioned"}:{BindingKind}:" +
        $"{DraftSessionId?.Value.ToString("N") ?? "none"}:{NestedGraphHash}";

    public static ResolvedCanvasKey Create(
        CanvasId canvasId,
        SceneVersionId? versionId,
        SceneVersionBinding binding,
        IEnumerable<ResolvedCanvasKey>? nestedCanvases = null)
    {
        if (canvasId.IsEmpty)
            throw new ArgumentException("Resolved canvas identity requires a logical canvas id.", nameof(canvasId));

        binding.Validate();
        var builder = new StringBuilder();
        builder.Append(canvasId.Value.ToString("N"))
            .Append('|').Append(versionId?.Value.ToString("N") ?? "unversioned")
            .Append('|').Append(binding.Kind)
            .Append('|').Append(binding.DraftSessionId?.Value.ToString("N") ?? "none")
            .Append('|').Append(binding.ExplicitVersionId?.Value.ToString("N") ?? "none");

        if (nestedCanvases is not null)
        {
            foreach (var nested in nestedCanvases)
                builder.Append("|nested:").Append(nested.StableValue);
        }

        var graphHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
        return new ResolvedCanvasKey(canvasId, versionId, binding.Kind, binding.DraftSessionId, graphHash);
    }

    public static ResolvedCanvasKey Unversioned(CanvasId canvasId) =>
        Create(canvasId, null, SceneVersionBinding.Published);

    public ResolvedCanvasKey Derive(string discriminator)
    {
        if (IsEmpty)
            throw new InvalidOperationException("Cannot derive a physical key from an empty resolved canvas key.");
        if (string.IsNullOrWhiteSpace(discriminator))
            throw new ArgumentException("A physical key discriminator is required.", nameof(discriminator));

        var hash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(StableValue + "|derived:" + discriminator)));
        return this with { NestedGraphHash = hash };
    }

    public override string ToString() => StableValue;
}
