namespace WTK.MediaForge.Composition;

public sealed class MediaForgeUnsupportedFeatureException : NotSupportedException
{
    public MediaForgeUnsupportedFeatureException(string featureCode, string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(featureCode);
        FeatureCode = featureCode;
    }

    public string FeatureCode { get; }
}
