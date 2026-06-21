namespace WTK.MediaForge.Composition.Validation;

public static class ProjectValidationResultExtensions
{
    public static void ThrowIfInvalid(this ProjectValidationResult result)
    {
        if (result.IsValid)
            return;

        throw new MediaForgeProjectValidationException(result);
    }
}
