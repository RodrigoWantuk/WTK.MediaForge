using WTK.MediaForge.Studio.DesignData;
using WTK.MediaForge.Studio.ViewModels;

namespace WTK.MediaForge.Studio.Services;

public static class StudioBootstrapper
{
    public static StudioShellViewModel CreateShellViewModel()
    {
        return StudioDesignData.CreateShellViewModel();
    }
}
