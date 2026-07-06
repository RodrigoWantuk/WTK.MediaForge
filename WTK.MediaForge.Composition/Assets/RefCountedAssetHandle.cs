namespace WTK.MediaForge.Composition.Assets;

internal sealed class RefCountedAssetHandle<T> : IDisposable
    where T : class
{
    private readonly T _value;
    private readonly Action<T> _release;
    private int _disposed;

    internal RefCountedAssetHandle(T value, Action<T> release)
    {
        _value = value ?? throw new ArgumentNullException(nameof(value));
        _release = release ?? throw new ArgumentNullException(nameof(release));
    }

    public T Value => _value;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _release(_value);
    }
}
