namespace WTK.MediaForge.Core.Identifiers;

public readonly record struct MediaSourceTypeId(string Value)
{
    public static readonly MediaSourceTypeId DesktopCapture = new("wtk.desktop.capture");
    public static readonly MediaSourceTypeId ImageFile = new("wtk.image.file");
    public static readonly MediaSourceTypeId VideoFile = new("wtk.video.file");

    public static MediaSourceTypeId From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("MediaSourceTypeId cannot be empty.", nameof(value));

        return new MediaSourceTypeId(value);
    }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public override string ToString() => Value;
}
