using Vortice.MediaFoundation;

namespace WTK.MediaForge.Windows.Media;

internal sealed class MediaFoundationRuntimeLease : IDisposable
{
    private int _disposed;

    internal MediaFoundationRuntimeLease()
    {
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        MediaFoundationRuntime.Release();
    }
}

internal static class MediaFoundationRuntime
{
    private static readonly object Gate = new();
    private static int _referenceCount;

    public static MediaFoundationRuntimeLease Acquire()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Media Foundation requires Windows.");

        lock (Gate)
        {
            if (_referenceCount == 0)
                MediaFactory.MFStartup(true).CheckError();

            _referenceCount++;
            return new MediaFoundationRuntimeLease();
        }
    }

    internal static int ReferenceCountForTests
    {
        get
        {
            lock (Gate)
                return _referenceCount;
        }
    }

    internal static void Release()
    {
        lock (Gate)
        {
            if (_referenceCount <= 0)
                throw new InvalidOperationException("Media Foundation runtime was released without a matching acquire.");

            _referenceCount--;
            if (_referenceCount == 0)
                MediaFactory.MFShutdown().CheckError();
        }
    }
}
