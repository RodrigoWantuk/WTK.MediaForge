using Vortice.MediaFoundation;
using WTK.MediaForge.Composition;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Composition.Sources.Settings;
using WTK.MediaForge.Windows.Media;

namespace WTK.MediaForge.Windows;

internal sealed record WindowsWebcamDeviceInfo(
    string DeviceId,
    string FriendlyName);

internal static class WindowsWebcamDeviceEnumerator
{
    public static IReadOnlyList<WindowsWebcamDeviceInfo> Enumerate()
    {
        if (!OperatingSystem.IsWindows())
            return [];

        using var runtime = MediaFoundationRuntime.Acquire();
        using var attributes = MediaFactory.MFCreateAttributes(1);
        attributes.Set(CaptureDeviceAttributeKeys.SourceType, CaptureDeviceAttributeKeys.SourceTypeVidcap)
            .CheckError();

        using var devices = MediaFactory.MFEnumDeviceSources(attributes);
        var result = new List<WindowsWebcamDeviceInfo>();

        foreach (var device in devices)
        {
            try
            {
                var symbolicLink = ReadString(device, CaptureDeviceAttributeKeys.SourceTypeVidcapSymbolicLink);
                var friendlyName = ReadString(device, CaptureDeviceAttributeKeys.FriendlyName);

                if (!string.IsNullOrWhiteSpace(symbolicLink))
                {
                    result.Add(new WindowsWebcamDeviceInfo(
                        symbolicLink,
                        string.IsNullOrWhiteSpace(friendlyName) ? "Webcam" : friendlyName));
                }
            }
            finally
            {
                device.Dispose();
            }
        }

        return result;
    }

    public static WindowsWebcamDeviceInfo Resolve(WebcamSourceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var devices = Enumerate();
        if (devices.Count == 0)
        {
            throw new MediaForgeUnsupportedFeatureException(
                $"source.{MediaSourceTypes.Webcam.Value}",
                "No Media Foundation video capture devices are available.");
        }

        if (string.IsNullOrWhiteSpace(settings.DeviceId) ||
            settings.DeviceId.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            return devices[0];
        }

        var requested = devices.FirstOrDefault(device =>
            device.DeviceId.Equals(settings.DeviceId, StringComparison.OrdinalIgnoreCase) ||
            device.FriendlyName.Equals(settings.DeviceId, StringComparison.OrdinalIgnoreCase));

        if (requested is not null)
            return requested;

        throw new MediaForgeUnsupportedFeatureException(
            $"source.{MediaSourceTypes.Webcam.Value}",
            $"Media Foundation video capture device '{settings.DeviceId}' was not found.");
    }

    private static string ReadString(IMFAttributes attributes, Guid key)
    {
        try
        {
            return attributes.GetAllocatedString(key) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
