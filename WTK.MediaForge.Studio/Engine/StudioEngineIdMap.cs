using System.Security.Cryptography;
using System.Text;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Studio.Engine;

public static class StudioEngineIdMap
{
    private const string NamespacePrefix = "wtk.mediaforge.studio.engine-id.v1";

    public static CanvasId CanvasId(string studioSceneId) =>
        WTK.MediaForge.Core.Identifiers.CanvasId.From(CreateStableGuid("scene", studioSceneId));

    public static DrawObjectId DrawObjectId(string studioLayerId) =>
        WTK.MediaForge.Core.Identifiers.DrawObjectId.From(CreateStableGuid("layer", studioLayerId));

    public static SourceId SourceId(string studioSourceId) =>
        WTK.MediaForge.Core.Identifiers.SourceId.From(CreateStableGuid("source", studioSourceId));

    public static RenderOutputId RenderOutputId(string studioOutputId) =>
        WTK.MediaForge.Core.Identifiers.RenderOutputId.From(CreateStableGuid("output", studioOutputId));

    public static EffectId EffectId(string studioEffectId) =>
        WTK.MediaForge.Core.Identifiers.EffectId.From(CreateStableGuid("effect", studioEffectId));

    private static Guid CreateStableGuid(string scope, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (Guid.TryParse(value, out var explicitGuid) && explicitGuid != Guid.Empty)
            return explicitGuid;

        var input = Encoding.UTF8.GetBytes($"{NamespacePrefix}:{scope}:{value}");
        var hash = SHA256.HashData(input);
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);

        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return new Guid(bytes);
    }
}
