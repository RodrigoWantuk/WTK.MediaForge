using System.Text;
using WTK.MediaForge.Core.Capture;
using WTK.MediaForge.Graphics.Vulkan;

namespace WMF.Testing;

internal static class CaptureDiagnosticsFormatter
{
    public static string BuildStartup(
        CaptureSourceInfo source,
        CaptureSessionInfo? session,
        VulkanRendererInfo? renderer)
    {
        var builder = new StringBuilder();

        AppendSource(builder, source);

        if (session is not null)
        {
            AppendSession(builder, session);

            bool logicalTextureMatch =
                source.LogicalSize.Width == session.DuplicationTextureSize.Width &&
                source.LogicalSize.Height == session.DuplicationTextureSize.Height;

            builder.AppendLine(
                $"Size check: logical={source.LogicalSize} | dupTex={session.DuplicationTextureSize} | logical==dup={logicalTextureMatch}");
        }

        AppendRenderer(builder, session, renderer, resolvedShaderRotation: null);

        return builder.ToString().TrimEnd();
    }

    private static void AppendSource(StringBuilder builder, CaptureSourceInfo source)
    {
        builder.AppendLine(
            $"Output: {source.OutputName} | adapter[{source.AdapterIndex}] {source.AdapterName} | outputIndex={source.OutputIndex}");
        builder.AppendLine(
            $"Desktop: rect={source.DesktopRect} | logical={source.LogicalSize} | DXGI rot={source.Rotation} | enum LUID={source.AdapterLuid}");
    }

    private static void AppendSession(StringBuilder builder, CaptureSessionInfo session)
    {
        builder.AppendLine(
            $"Duplication: tex={session.DuplicationTextureSize} | format={session.TextureFormat} | refresh={session.RefreshRateNumerator}/{session.RefreshRateDenominator} | capture LUID={session.CaptureAdapterLuid}");
    }

    private static void AppendRenderer(
        StringBuilder builder,
        CaptureSessionInfo? session,
        VulkanRendererInfo? renderer,
        int? resolvedShaderRotation)
    {
        if (renderer is null)
            return;

        string vulkanLuidText = renderer.DeviceLuidValid
            ? renderer.DeviceLuid.ToString()
            : "n/a (invalid)";

        string luidMatchText;
        if (!renderer.DeviceLuidValid)
        {
            luidMatchText = "n/a (Vulkan LUID unavailable)";
        }
        else if (session is null)
        {
            luidMatchText = "pending";
        }
        else
        {
            luidMatchText = (session.CaptureAdapterLuid == renderer.DeviceLuid).ToString();
        }

        string rotationText = resolvedShaderRotation.HasValue
            ? $"{resolvedShaderRotation.Value * 90}° (idx={resolvedShaderRotation.Value})"
            : "pending";

        builder.Append(
            $"Vulkan: {renderer.DeviceName} | LUID={vulkanLuidText} | LUID match={luidMatchText} | swapchain={renderer.SwapchainWidth}x{renderer.SwapchainHeight} {renderer.SwapchainFormat} | shaderRot={rotationText}");
    }
}
