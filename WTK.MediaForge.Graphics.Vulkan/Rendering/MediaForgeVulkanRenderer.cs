using System.Buffers;
using System.Collections.Concurrent;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Diagnostics;
using WTK.MediaForge.Graphics.D3D11;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal sealed unsafe class MediaForgeVulkanRenderer : IRenderBackend
{
    private const int MaxExternalTextureImportsPerSubmit = 128;

    private readonly RenderThreadGuard _threadGuard;
    private readonly VulkanHeadlessDevice _deviceContext;
    private readonly VulkanExternalTextureRegistry _textureRegistry;
    private readonly IMediaForgeDiagnosticsSink? _diagnostics;
    private readonly IVulkanRendererFaultInjector _faultInjector;
    private readonly Func<VulkanHeadlessDevice, FrameSize, IVulkanOffscreenRenderTarget> _offscreenTargetFactory;
    private readonly Action _disposeDevice;
    private readonly VulkanCp1ShaderPipelines _cp1Pipelines;
    private readonly ConcurrentDictionary<RenderOutputId, RenderOutputBindingSnapshot> _bindings = new();
    private readonly ConcurrentDictionary<RenderOutputId, VulkanOffscreenTargetHandle> _offscreenTargets = new();
    private int _disposed;

    internal MediaForgeVulkanRenderer(
        RenderThreadGuard threadGuard,
        IMediaForgeDiagnosticsSink? diagnostics,
        IVulkanRendererFaultInjector faultInjector)
        : this(threadGuard, VulkanHeadlessDevice.Create(), diagnostics, faultInjector)
    {
    }

    internal MediaForgeVulkanRenderer(
        RenderThreadGuard threadGuard,
        VulkanHeadlessDevice deviceContext,
        IMediaForgeDiagnosticsSink? diagnostics,
        IVulkanRendererFaultInjector faultInjector,
        Func<VulkanHeadlessDevice, FrameSize, IVulkanOffscreenRenderTarget>? offscreenTargetFactory = null,
        Action? disposeDevice = null)
    {
        _threadGuard = threadGuard ?? throw new ArgumentNullException(nameof(threadGuard));
        _deviceContext = deviceContext ?? throw new ArgumentNullException(nameof(deviceContext));
        _diagnostics = diagnostics;
        _faultInjector = faultInjector ?? throw new ArgumentNullException(nameof(faultInjector));
        _offscreenTargetFactory = offscreenTargetFactory ?? CreateOffscreenRenderTarget;
        _disposeDevice = disposeDevice ?? _deviceContext.Dispose;
        _textureRegistry = new VulkanExternalTextureRegistry(_deviceContext, diagnostics);
        _cp1Pipelines = new VulkanCp1ShaderPipelines(_deviceContext, diagnostics);
    }

    internal VulkanExternalTextureRegistry TextureRegistry => _textureRegistry;

    internal int TextureRegistryActiveLeaseCount => _textureRegistry.ActiveLeaseCount;

    internal int OffscreenTargetCount =>
        _offscreenTargets.Values.Count(handle => handle.IsAlive);

    internal bool TryGetOffscreenTargetSize(RenderOutputId outputId, out FrameSize size)
    {
        if (_offscreenTargets.TryGetValue(outputId, out var handle) && handle.IsAlive)
        {
            size = handle.Target.Size;
            return true;
        }

        size = default;
        return false;
    }

    internal bool TryGetOffscreenTargetLayout(RenderOutputId outputId, out ImageLayout layout)
    {
        if (_offscreenTargets.TryGetValue(outputId, out var handle) &&
            handle.IsAlive &&
            handle.Target is VulkanOffscreenRenderTarget target)
        {
            layout = target.CurrentLayout;
            return true;
        }

        layout = default;
        return false;
    }

    internal bool TryReadOffscreenPixel(
        RenderOutputId outputId,
        uint x,
        uint y,
        out VulkanReadbackPixel pixel)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _threadGuard.AssertOnRenderThread();

        if (_offscreenTargets.TryGetValue(outputId, out var handle) &&
            handle.IsAlive &&
            handle.Target is VulkanOffscreenRenderTarget target)
        {
            pixel = VulkanOffscreenReadback.ReadPixel(target, x, y);
            return true;
        }

        pixel = default;
        return false;
    }

    internal IVulkanRendererFaultInjector FaultInjector => _faultInjector;

    public int SubmitCount => Volatile.Read(ref _submitCount);

    private int _submitCount;

    private static IVulkanOffscreenRenderTarget CreateOffscreenRenderTarget(
        VulkanHeadlessDevice deviceContext,
        FrameSize size) =>
        new VulkanOffscreenRenderTarget(deviceContext, size);

    internal static bool TryCreate(
        RenderThreadGuard threadGuard,
        IMediaForgeDiagnosticsSink? diagnostics,
        IVulkanRendererFaultInjector faultInjector,
        out MediaForgeVulkanRenderer? renderer)
    {
        ArgumentNullException.ThrowIfNull(threadGuard);
        ArgumentNullException.ThrowIfNull(faultInjector);

        try
        {
            renderer = new MediaForgeVulkanRenderer(threadGuard, diagnostics, faultInjector);
            return true;
        }
        catch
        {
            renderer = null;
            return false;
        }
    }

    public void BindOutput(RenderOutputBindingSnapshot binding)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _threadGuard.AssertOnRenderThread();
        ArgumentNullException.ThrowIfNull(binding);

        if (binding.TargetKind == RenderTargetKind.Offscreen)
        {
            if (binding.SurfaceSize.Width == 0 || binding.SurfaceSize.Height == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(binding),
                    "Offscreen output binding requires non-zero surface dimensions.");
            }

            if (_offscreenTargets.TryRemove(binding.OutputId, out var existing))
                existing.Retire();

            _offscreenTargets[binding.OutputId] = new VulkanOffscreenTargetHandle(
                _offscreenTargetFactory(_deviceContext, binding.SurfaceSize));
        }

        _bindings[binding.OutputId] = binding;
    }

    public void UnbindOutput(RenderOutputId outputId)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _threadGuard.AssertOnRenderThread();

        _bindings.TryRemove(outputId, out _);

        if (_offscreenTargets.TryRemove(outputId, out var target))
            target.Retire();
    }

    public void ResizeOutput(RenderOutputId outputId, FrameSize surfaceSize)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _threadGuard.AssertOnRenderThread();

        if (surfaceSize.Width == 0 || surfaceSize.Height == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(surfaceSize),
                "Output surface dimensions must be greater than zero.");
        }

        if (_bindings.TryGetValue(outputId, out var existing))
        {
            _bindings[outputId] = new RenderOutputBindingSnapshot
            {
                OutputId = existing.OutputId,
                TargetKind = existing.TargetKind,
                NativeHandle = existing.NativeHandle,
                SurfaceSize = surfaceSize,
                BindingVersion = existing.BindingVersion + 1
            };

            if (existing.TargetKind == RenderTargetKind.Offscreen &&
                _offscreenTargets.TryGetValue(outputId, out var handle) &&
                handle.IsAlive &&
                handle.Target.Size != surfaceSize)
            {
                _offscreenTargets.TryRemove(outputId, out _);
                handle.Retire();
                _offscreenTargets[outputId] = new VulkanOffscreenTargetHandle(
                    _offscreenTargetFactory(_deviceContext, surfaceSize));
            }
            else if (existing.TargetKind == RenderTargetKind.Offscreen &&
                     _offscreenTargets.TryGetValue(outputId, out var resizeHandle) &&
                     resizeHandle.IsAlive)
            {
                resizeHandle.Target.Resize(surfaceSize);
            }
        }
    }

    public IRenderFrameSubmission Submit(RenderFrameSnapshot snapshot)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _threadGuard.AssertOnRenderThread();
        ArgumentNullException.ThrowIfNull(snapshot);

        Interlocked.Increment(ref _submitCount);

        var handles = RenderFrameSnapshotGpuFrames.CollectD3D11SharedTextures(snapshot);

        if (handles.Count > MaxExternalTextureImportsPerSubmit)
        {
            throw new NotSupportedException(
                $"Submit supports at most {MaxExternalTextureImportsPerSubmit} external textures.");
        }

        List<VulkanExternalTextureLease>? textureLeases = null;
        VulkanSubmissionResourceScope? submissionResources = null;
        IReadOnlyList<IRenderedOutputSurfaceLease>? renderedOutputSurfaces = null;
        CommandBuffer commandBuffer = default;
        Fence fence = default;

        try
        {
            textureLeases = AcquireTextureLeases(handles);
            var imports = textureLeases.Select(lease => lease.Import).ToList();
            var previousLayouts = imports.Select(import => import.CurrentLayout).ToArray();
            PrepareOffscreenTargetsForSubmit(snapshot);
            var previousTargetLayouts = CaptureOffscreenTargetLayouts();
            submissionResources = _cp1Pipelines.CreateSubmissionResourceScope();

            try
            {
                lock (_deviceContext.CommandQueueGate)
                {
                    commandBuffer = BeginCommandBuffer();

                    renderedOutputSurfaces = VulkanCp1OffscreenCompositor.Compose(
                        _cp1Pipelines,
                        commandBuffer,
                        snapshot,
                        _offscreenTargets,
                        textureLeases,
                        submissionResources);

                    if (_deviceContext.Vk.EndCommandBuffer(commandBuffer) != Result.Success)
                        throw new InvalidOperationException("vkEndCommandBuffer failed.");

                    fence = CreateFence();
                    SubmitCommandBuffer(commandBuffer, imports, fence);
                }
            }
            catch (Exception submitEx)
            {
                List<Exception>? cleanupErrors = null;

                try
                {
                    submissionResources.Dispose();
                }
                catch (Exception cleanupEx)
                {
                    (cleanupErrors ??= []).Add(cleanupEx);
                }

                submissionResources = null;

                for (var i = 0; i < imports.Count; i++)
                    imports[i].SetLayout(previousLayouts[i]);

                RestoreOffscreenTargetLayouts(previousTargetLayouts);

                if (cleanupErrors is not null)
                {
                    cleanupErrors.Insert(0, submitEx);
                    throw new AggregateException("Failed to clean up failed Vulkan submit.", cleanupErrors);
                }

                throw;
            }

            return new VulkanRenderFrameSubmission(
                _deviceContext,
                snapshot,
                commandBuffer,
                fence,
                textureLeases,
                submissionResources,
                renderedOutputSurfaces,
                _diagnostics);
        }
        catch (Exception submitFailure)
        {
            List<Exception>? cleanupErrors = null;

            try
            {
                DisposeRenderedOutputSurfaces(renderedOutputSurfaces);
            }
            catch (Exception ex)
            {
                (cleanupErrors ??= []).Add(ex);
            }

            try
            {
                submissionResources?.Dispose();
            }
            catch (Exception ex)
            {
                (cleanupErrors ??= []).Add(ex);
            }

            try
            {
                CleanupFailedSubmit(textureLeases, commandBuffer, fence);
            }
            catch (Exception ex)
            {
                (cleanupErrors ??= []).Add(ex);
            }

            if (cleanupErrors is not null)
            {
                cleanupErrors.Insert(0, submitFailure);
                throw new AggregateException("Failed to clean up rendered output surfaces after Vulkan submit failure.", cleanupErrors);
            }

            throw;
        }
    }

    private static void DisposeRenderedOutputSurfaces(
        IReadOnlyList<IRenderedOutputSurfaceLease>? surfaces)
    {
        if (surfaces is null)
            return;

        List<Exception>? errors = null;

        foreach (var surface in surfaces)
        {
            try
            {
                surface.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }
        }

        if (errors is not null)
            throw new AggregateException("Failed to dispose rendered output surfaces after submit failure.", errors);
    }

    private Dictionary<VulkanOffscreenRenderTarget, ImageLayout> CaptureOffscreenTargetLayouts()
    {
        var layouts = new Dictionary<VulkanOffscreenRenderTarget, ImageLayout>();

        foreach (var handle in _offscreenTargets.Values)
        {
            if (!handle.IsAlive || handle.Target is not VulkanOffscreenRenderTarget target)
                continue;

            layouts[target] = target.CurrentLayout;
        }

        return layouts;
    }

    private static void RestoreOffscreenTargetLayouts(
        IReadOnlyDictionary<VulkanOffscreenRenderTarget, ImageLayout> layouts)
    {
        foreach (var (target, layout) in layouts)
            target.CurrentLayout = layout;
    }

    private void CleanupFailedSubmit(
        List<VulkanExternalTextureLease>? textureLeases,
        CommandBuffer commandBuffer,
        Fence fence)
    {
        var vk = _deviceContext.Vk;
        var device = _deviceContext.Device;
        var commandBufferFreed = false;
        var fenceDestroyed = false;
        var textureLeaseDisposeCount = 0;

        if (fence.Handle != 0)
        {
            vk.DestroyFence(device, fence, null);
            fenceDestroyed = true;
        }

        if (commandBuffer.Handle != 0)
        {
            var localCommandBuffer = commandBuffer;
            lock (_deviceContext.CommandQueueGate)
            {
                vk.FreeCommandBuffers(device, _deviceContext.CommandPool, 1, &localCommandBuffer);
            }

            commandBufferFreed = true;
        }

        if (textureLeases is not null)
        {
            foreach (var lease in textureLeases)
            {
                try
                {
                    lease.Dispose();
                    textureLeaseDisposeCount++;
                }
                catch (Exception ex)
                {
                    MediaForgeDiagnostics.Report(
                        _diagnostics,
                        MediaForgeDiagnosticSeverity.Error,
                        "vulkan.texture_lease_dispose_failed",
                        "Failed to dispose texture lease after failed submit.",
                        nameof(MediaForgeVulkanRenderer),
                        ex);
                }
            }
        }

        _faultInjector.AfterFailedSubmitCleanup(
            commandBufferFreed,
            fenceDestroyed,
            textureLeaseDisposeCount);
    }

    /// <summary>
    /// No-op: submission ownership belongs to <see cref="PendingRenderSubmissionTracker"/>.
    /// Do not create untracked internal submissions in this backend.
    /// Shutdown waits each submission fence via the tracker.
    /// </summary>
    public ValueTask WaitIdleAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _threadGuard.AssertOnRenderThread();

        _ = timeout;
        _ = cancellationToken;

        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        if (_textureRegistry.ActiveLeaseCount > 0)
        {
            throw new InvalidOperationException(
                "Cannot dispose MediaForgeVulkanRenderer while texture leases are active. " +
                "All render submissions must be completed and disposed through PendingRenderSubmissionTracker first.");
        }

        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        List<Exception>? errors = null;

        foreach (var target in _offscreenTargets.Values)
        {
            try
            {
                target.Retire();
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }
        }

        _offscreenTargets.Clear();

        try
        {
            _cp1Pipelines.Dispose();
        }
        catch (Exception ex)
        {
            (errors ??= []).Add(ex);
        }

        try
        {
            _textureRegistry.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            (errors ??= []).Add(ex);
        }

        try
        {
            _disposeDevice();
        }
        catch (Exception ex)
        {
            (errors ??= []).Add(ex);
        }

        if (errors is not null)
            throw new AggregateException("Failed to dispose Vulkan renderer cleanly.", errors);
    }

    private List<VulkanExternalTextureLease> AcquireTextureLeases(IReadOnlyList<D3D11SharedTextureFrameHandle> handles)
    {
        var leases = new List<VulkanExternalTextureLease>(handles.Count);
        var acquireAttempt = 0;

        try
        {
            foreach (var handle in handles)
            {
                acquireAttempt++;
                _faultInjector.BeforeAcquireTexture(acquireAttempt);
                leases.Add(_textureRegistry.Acquire(handle));
            }

            return leases;
        }
        catch
        {
            foreach (var lease in leases)
            {
                try
                {
                    lease.Dispose();
                }
                catch (Exception ex)
                {
                    MediaForgeDiagnostics.Report(
                        _diagnostics,
                        MediaForgeDiagnosticSeverity.Error,
                        "vulkan.texture_lease_dispose_failed",
                        "Failed to dispose texture lease after partial acquire failure.",
                        nameof(MediaForgeVulkanRenderer),
                        ex);
                }
            }

            throw;
        }
    }

    private void PrepareOffscreenTargetsForSubmit(RenderFrameSnapshot snapshot)
    {
        foreach (var output in snapshot.Outputs)
        {
            if (!_bindings.TryGetValue(output.Id, out var binding) ||
                binding.TargetKind != RenderTargetKind.Offscreen)
            {
                continue;
            }

            if (!_offscreenTargets.TryGetValue(output.Id, out var handle) ||
                !handle.IsAlive ||
                !handle.HasSubmissionReferences)
            {
                continue;
            }

            var replacement = new VulkanOffscreenTargetHandle(
                _offscreenTargetFactory(_deviceContext, handle.Target.Size));

            _offscreenTargets[output.Id] = replacement;
            handle.Retire();
        }
    }

    private CommandBuffer BeginCommandBuffer()
    {
        var allocInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _deviceContext.CommandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1
        };

        CommandBuffer commandBuffer;

        lock (_deviceContext.CommandQueueGate)
        {
            if (_deviceContext.Vk.AllocateCommandBuffers(_deviceContext.Device, &allocInfo, out commandBuffer) !=
                Result.Success)
            {
                throw new InvalidOperationException("vkAllocateCommandBuffers failed.");
            }

            var beginInfo = new CommandBufferBeginInfo
            {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.OneTimeSubmitBit
            };

            if (_deviceContext.Vk.BeginCommandBuffer(commandBuffer, &beginInfo) != Result.Success)
                throw new InvalidOperationException("vkBeginCommandBuffer failed.");
        }

        return commandBuffer;
    }

    private Fence CreateFence()
    {
        var fenceInfo = new FenceCreateInfo
        {
            SType = StructureType.FenceCreateInfo,
            Flags = FenceCreateFlags.None
        };

        if (_deviceContext.Vk.CreateFence(_deviceContext.Device, &fenceInfo, null, out Fence fence) != Result.Success)
            throw new InvalidOperationException("vkCreateFence failed.");

        return fence;
    }

    private void SubmitCommandBuffer(
        CommandBuffer commandBuffer,
        IReadOnlyList<VulkanD3D11TextureImport> imports,
        Fence fence)
    {
        var vk = _deviceContext.Vk;
        var device = _deviceContext.Device;

        DeviceMemory[]? acquireSyncs = null;
        DeviceMemory[]? releaseSyncs = null;
        ulong[]? acquireKeys = null;
        ulong[]? releaseKeys = null;
        uint[]? acquireTimeouts = null;

        try
        {
            void* submitPNext = null;
            Win32KeyedMutexAcquireReleaseInfoKHR keyedMutexInfo = default;

            if (imports.Count > 0)
            {
                acquireSyncs = ArrayPool<DeviceMemory>.Shared.Rent(imports.Count);
                releaseSyncs = ArrayPool<DeviceMemory>.Shared.Rent(imports.Count);
                acquireKeys = ArrayPool<ulong>.Shared.Rent(imports.Count);
                releaseKeys = ArrayPool<ulong>.Shared.Rent(imports.Count);
                acquireTimeouts = ArrayPool<uint>.Shared.Rent(imports.Count);

                for (var i = 0; i < imports.Count; i++)
                {
                    acquireSyncs[i] = imports[i].Memory;
                    releaseSyncs[i] = imports[i].Memory;
                    acquireKeys[i] = imports[i].SourceHandle.ProducerAcquireKey;
                    releaseKeys[i] = D3D11SharedTextureSyncKeys.Producer;
                    acquireTimeouts[i] = 1_000_000_000;
                }

                fixed (DeviceMemory* acquireSyncPtr = acquireSyncs)
                fixed (DeviceMemory* releaseSyncPtr = releaseSyncs)
                fixed (ulong* acquireKeysPtr = acquireKeys)
                fixed (ulong* releaseKeysPtr = releaseKeys)
                fixed (uint* acquireTimeoutsPtr = acquireTimeouts)
                {
                    keyedMutexInfo = new Win32KeyedMutexAcquireReleaseInfoKHR
                    {
                        SType = StructureType.Win32KeyedMutexAcquireReleaseInfoKhr,
                        AcquireCount = (uint)imports.Count,
                        PAcquireSyncs = acquireSyncPtr,
                        PAcquireKeys = acquireKeysPtr,
                        PAcquireTimeouts = acquireTimeoutsPtr,
                        ReleaseCount = (uint)imports.Count,
                        PReleaseSyncs = releaseSyncPtr,
                        PReleaseKeys = releaseKeysPtr
                    };

                    submitPNext = &keyedMutexInfo;

                    var commandBuffers = stackalloc CommandBuffer[1];
                    commandBuffers[0] = commandBuffer;

                    var submitInfo = new SubmitInfo
                    {
                        SType = StructureType.SubmitInfo,
                        PNext = submitPNext,
                        CommandBufferCount = 1,
                        PCommandBuffers = commandBuffers
                    };

                    lock (_deviceContext.CommandQueueGate)
                    {
                        _faultInjector.BeforeQueueSubmit();

                        if (vk.QueueSubmit(_deviceContext.GraphicsQueue, 1, &submitInfo, fence) != Result.Success)
                            throw new InvalidOperationException("vkQueueSubmit failed.");
                    }
                }
            }
            else
            {
                var commandBuffers = stackalloc CommandBuffer[1];
                commandBuffers[0] = commandBuffer;

                var submitInfo = new SubmitInfo
                {
                    SType = StructureType.SubmitInfo,
                    PNext = submitPNext,
                    CommandBufferCount = 1,
                    PCommandBuffers = commandBuffers
                };

                lock (_deviceContext.CommandQueueGate)
                {
                    _faultInjector.BeforeQueueSubmit();

                    if (vk.QueueSubmit(_deviceContext.GraphicsQueue, 1, &submitInfo, fence) != Result.Success)
                        throw new InvalidOperationException("vkQueueSubmit failed.");
                }
            }

            foreach (var import in imports)
                import.SourceHandle.NotifyVulkanReleasedToProducer();
        }
        finally
        {
            if (acquireSyncs is not null)
                ArrayPool<DeviceMemory>.Shared.Return(acquireSyncs, clearArray: true);

            if (releaseSyncs is not null)
                ArrayPool<DeviceMemory>.Shared.Return(releaseSyncs, clearArray: true);

            if (acquireKeys is not null)
                ArrayPool<ulong>.Shared.Return(acquireKeys, clearArray: true);

            if (releaseKeys is not null)
                ArrayPool<ulong>.Shared.Return(releaseKeys, clearArray: true);

            if (acquireTimeouts is not null)
                ArrayPool<uint>.Shared.Return(acquireTimeouts, clearArray: true);
        }
    }
}
