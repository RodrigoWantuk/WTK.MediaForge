using WTK.MediaForge.Diagnostics;
using WTK.MediaForge.Graphics.D3D11;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

public sealed class VulkanExternalTextureRegistry : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly VulkanHeadlessDevice _deviceContext;
    private readonly IMediaForgeDiagnosticsSink? _diagnostics;
    private readonly Dictionary<VulkanExternalTextureKey, RegistryEntry> _entries = new();
    private bool _disposed;

    internal VulkanExternalTextureRegistry(
        VulkanHeadlessDevice deviceContext,
        IMediaForgeDiagnosticsSink? diagnostics = null)
    {
        _deviceContext = deviceContext ?? throw new ArgumentNullException(nameof(deviceContext));
        _diagnostics = diagnostics;
    }

    internal int EntryCount
    {
        get
        {
            lock (_gate)
                return _entries.Count;
        }
    }

    public VulkanExternalTextureLease Acquire(D3D11SharedTextureFrameHandle handle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(handle);

        if (!handle.HasSharedHandle)
        {
            throw new ObjectDisposedException(
                nameof(handle),
                "D3D11 shared texture handle is closed or unavailable.");
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (!handle.HasSharedHandle)
            {
                throw new ObjectDisposedException(
                    nameof(handle),
                    "D3D11 shared texture handle is closed or unavailable.");
            }

            var key = VulkanExternalTextureKey.From(handle);

            if (!_entries.TryGetValue(key, out var entry))
            {
                VulkanD3D11TextureImport import;

                try
                {
                    import = VulkanD3D11TextureImport.Import(_deviceContext, handle);
                }
                catch (Exception ex)
                {
                    MediaForgeDiagnostics.Report(
                        _diagnostics,
                        MediaForgeDiagnosticSeverity.Error,
                        "vulkan.texture_import_failed",
                        ex.Message,
                        nameof(VulkanExternalTextureRegistry),
                        ex);
                    throw;
                }

                entry = new RegistryEntry(key, handle, import);
                _entries[key] = entry;
            }

            entry.RefCount++;
            return new VulkanExternalTextureLease(entry, this);
        }
    }

    internal void Release(VulkanExternalTextureKey key)
    {
        VulkanD3D11TextureImport? importToDispose = null;

        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var entry))
                return;

            entry.RefCount--;

            if (entry.RefCount < 0)
                throw new InvalidOperationException("External texture refcount underflow.");

            if (entry.RefCount == 0 && entry.SourceHandle.IsRetired)
            {
                _entries.Remove(key);
                importToDispose = entry.Import;
            }
        }

        importToDispose?.Dispose();
    }

    public void CollectUnused()
    {
        lock (_gate)
        {
            List<VulkanExternalTextureKey>? toRemove = null;

            foreach (var (key, entry) in _entries)
            {
                if (entry.SourceHandle.IsRetired && entry.RefCount == 0)
                {
                    entry.Import.Dispose();
                    (toRemove ??= []).Add(key);
                }
            }

            if (toRemove is null)
                return;

            foreach (var key in toRemove)
                _entries.Remove(key);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
                return ValueTask.CompletedTask;

            _disposed = true;

            foreach (var entry in _entries.Values)
                entry.Import.Dispose();

            _entries.Clear();
        }

        return ValueTask.CompletedTask;
    }

    internal sealed class RegistryEntry
    {
        public RegistryEntry(
            VulkanExternalTextureKey key,
            D3D11SharedTextureFrameHandle sourceHandle,
            VulkanD3D11TextureImport import)
        {
            Key = key;
            SourceHandle = sourceHandle;
            Import = import;
        }

        public VulkanExternalTextureKey Key { get; }

        public D3D11SharedTextureFrameHandle SourceHandle { get; }

        public VulkanD3D11TextureImport Import { get; }

        public int RefCount { get; set; }
    }
}
