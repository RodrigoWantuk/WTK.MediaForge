namespace WTK.MediaForge.Testing;

public static class GpuTestProcessLease
{
    private const string MutexName = @"Local\WTK.MediaForge.GpuIntegrationTests";
    private static readonly TimeSpan AcquireTimeout = TimeSpan.FromMinutes(5);
    private static readonly object Gate = new();
    private static Mutex? _mutex;
    private static bool _ownsMutex;

    public static void AcquireForCurrentTestProcess()
    {
        lock (Gate)
        {
            if (_ownsMutex)
                return;

            _mutex ??= new Mutex(initiallyOwned: false, MutexName);
            try
            {
                _ownsMutex = _mutex.WaitOne(AcquireTimeout);
            }
            catch (AbandonedMutexException)
            {
                _ownsMutex = true;
            }

            if (!_ownsMutex)
            {
                throw new TimeoutException(
                    $"Timed out after {AcquireTimeout} waiting for exclusive GPU integration test ownership.");
            }

            // Mutex ownership is thread-affine. Keep the handle rooted for the
            // lifetime of the test host and let Windows release it atomically
            // when the process exits, including abnormal test-host termination.
        }
    }
}
