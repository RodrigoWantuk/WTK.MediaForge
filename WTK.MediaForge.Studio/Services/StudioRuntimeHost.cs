using WTK.MediaForge.Composition.Runtime;

namespace WTK.MediaForge.Studio.Services;

public static class StudioRuntimeHost
{
    private static IMediaForgeRuntimeFactory? _runtimeFactory;

    public static void Configure(IMediaForgeRuntimeFactory runtimeFactory)
    {
        ArgumentNullException.ThrowIfNull(runtimeFactory);
        if (Interlocked.CompareExchange(ref _runtimeFactory, runtimeFactory, null) is not null)
            throw new InvalidOperationException("The Studio runtime factory is already configured.");
    }

    internal static IMediaForgeRuntimeFactory GetRequiredFactory() =>
        Volatile.Read(ref _runtimeFactory)
        ?? throw new InvalidOperationException("A platform host must configure the Studio runtime factory before the application starts.");
}
