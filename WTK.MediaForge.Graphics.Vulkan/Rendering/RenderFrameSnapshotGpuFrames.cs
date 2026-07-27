using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Graphics.D3D11;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal static class RenderFrameSnapshotGpuFrames
{
    public static IReadOnlyList<D3D11SharedTextureFrameHandle> CollectD3D11SharedTextures(
        RenderFrameSnapshot snapshot,
        IReadOnlySet<SourceId>? acquiredSourceIds = null)
        => CollectD3D11SharedTextures(
            snapshot,
            acquiredSourceIds?.ToDictionary(static sourceId => sourceId, static sourceId => $"source:{sourceId}"));

    public static IReadOnlyList<D3D11SharedTextureFrameHandle> CollectD3D11SharedTextures(
        RenderFrameSnapshot snapshot,
        IReadOnlyList<PhysicalRenderGraphOperation> physicalOperations)
    {
        ArgumentNullException.ThrowIfNull(physicalOperations);

        var acquisitions = physicalOperations
            .Where(static operation => operation.Kind == PhysicalRenderGraphOperationKind.AcquireSourceFrame)
            .Select(static operation => (operation.SourceId, operation.Key))
            .Where(static operation => operation.SourceId is not null)
            .ToArray();
        var acquisitionKeysBySource = new Dictionary<SourceId, string>();
        foreach (var (sourceId, operationKey) in acquisitions)
        {
            if (!acquisitionKeysBySource.TryAdd(sourceId!.Value, operationKey))
            {
                throw new InvalidOperationException(
                    $"Physical RenderGraph contains more than one source acquisition for source '{sourceId}'.");
            }
        }

        return CollectD3D11SharedTextures(snapshot, acquisitionKeysBySource);
    }

    private static IReadOnlyList<D3D11SharedTextureFrameHandle> CollectD3D11SharedTextures(
        RenderFrameSnapshot snapshot,
        IReadOnlyDictionary<SourceId, string>? acquisitionKeysBySource)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var handles = new List<D3D11SharedTextureFrameHandle>();
        var seen = new HashSet<VulkanExternalTextureKey>();
        var resolvedExternalTextures = new Dictionary<SourceId, VulkanExternalTextureKey>();

        foreach (var canvas in snapshot.Canvases)
            Collect(canvas.Objects, handles, seen, resolvedExternalTextures, acquisitionKeysBySource);

        return handles;
    }

    private static void Collect(
        IReadOnlyList<RenderDrawObjectSnapshot> objects,
        List<D3D11SharedTextureFrameHandle> handles,
        HashSet<VulkanExternalTextureKey> seen,
        Dictionary<SourceId, VulkanExternalTextureKey> resolvedExternalTextures,
        IReadOnlyDictionary<SourceId, string>? acquisitionKeysBySource)
    {
        foreach (var drawObject in objects)
        {
            switch (drawObject)
            {
                case RenderSourceLayerDrawObjectSnapshot sourceLayer
                    when sourceLayer.Enabled &&
                         (acquisitionKeysBySource is null || acquisitionKeysBySource.ContainsKey(sourceLayer.SourceId)) &&
                         sourceLayer.BoundFrame?.Handle is D3D11SharedTextureFrameHandle handle:
                    TryAdd(
                        handle,
                        sourceLayer.SourceId,
                        handles,
                        seen,
                        resolvedExternalTextures,
                        acquisitionKeysBySource);
                    break;

                case RenderCanvasDrawObjectSnapshot canvasDraw
                    when canvasDraw.Enabled && canvasDraw.NestedCanvas is not null:
                    Collect(
                        canvasDraw.NestedCanvas.Objects,
                        handles,
                        seen,
                        resolvedExternalTextures,
                        acquisitionKeysBySource);
                    break;
            }
        }
    }

    private static void TryAdd(
        D3D11SharedTextureFrameHandle handle,
        SourceId sourceId,
        List<D3D11SharedTextureFrameHandle> handles,
        HashSet<VulkanExternalTextureKey> seen,
        IDictionary<SourceId, VulkanExternalTextureKey> resolvedExternalTextures,
        IReadOnlyDictionary<SourceId, string>? acquisitionKeysBySource)
    {
        if (!handle.HasSharedHandle)
            return;

        var key = VulkanExternalTextureKey.From(handle);
        if (acquisitionKeysBySource is not null &&
            resolvedExternalTextures.TryGetValue(sourceId, out var previousKey) &&
            previousKey != key)
        {
            throw new InvalidOperationException(
                $"Physical source acquisition '{acquisitionKeysBySource[sourceId]}' resolved multiple external textures for source '{sourceId}'.");
        }

        if (acquisitionKeysBySource is not null)
            resolvedExternalTextures[sourceId] = key;

        if (!seen.Add(key))
            return;

        handles.Add(handle);
    }
}
