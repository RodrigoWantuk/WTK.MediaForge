using System.Collections.Immutable;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Silk.NET.Vulkan;
using WTK.MediaForge.Composition.Assets;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Diagnostics;
using WTK.MediaForge.Graphics.D3D11;
using WTK.MediaForge.Graphics.Vulkan.Rendering;
using WTK.MediaForge.Graphics.Vulkan.Text;
using Xunit;

namespace WTK.MediaForge.Graphics.Vulkan.Tests;

internal static class VulkanCompositionTestHarness
{
    internal static RenderFrameSnapshot CreateCp1Snapshot(
        D3D11SharedTextureFrameHandle sharedHandle,
        CanvasId canvasId,
        RenderOutputId outputId,
        FrameSize? canvasSize = null,
        FrameSize? outputSize = null,
        Transform2D? transform = null,
        LayoutMode layerLayoutMode = LayoutMode.Fit,
        LayoutMode outputCanvasLayoutMode = LayoutMode.Fit,
        ColorRgba? layerLetterboxColor = null,
        ColorRgba? outputLetterboxColor = null,
        ColorRgba? canvasBackgroundColor = null,
        float opacity = 1f,
        int sourceLayerCount = 1)
    {
        if (sourceLayerCount < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceLayerCount));

        var resolvedCanvasSize = canvasSize ?? new FrameSize(1920, 1080);
        var resolvedOutputSize = outputSize ?? new FrameSize(1280, 720);
        var resolvedTransform = transform ?? new Transform2D
        {
            Size = new CanvasSize(resolvedCanvasSize.Width, resolvedCanvasSize.Height)
        };
        var frame = new GpuFrameReference
        {
            Backend = GpuFrameBackend.D3D11SharedTexture,
            Handle = sharedHandle,
            TextureSize = sharedHandle.TextureSize,
            LogicalSize = sharedHandle.TextureSize,
            SourceId = SourceId.New(),
            FrameNumber = 1
        };
        var objects = Enumerable
            .Range(0, sourceLayerCount)
            .Select(_ => new RenderSourceLayerDrawObjectSnapshot
            {
                Id = DrawObjectId.New(),
                Name = "Desktop",
                SourceId = frame.SourceId,
                Transform = resolvedTransform,
                LayoutMode = layerLayoutMode,
                LetterboxColor = layerLetterboxColor ?? ColorRgba.Transparent,
                Opacity = opacity,
                BoundFrame = frame
            })
            .Cast<RenderDrawObjectSnapshot>()
            .ToImmutableArray();

        return new RenderFrameSnapshot
        {
            ProjectStateVersion = 1,
            Canvases =
            [
                new RenderCanvasSnapshot
                {
                    Id = canvasId,
                    Name = "Program",
                    Size = resolvedCanvasSize,
                    BackgroundColor = canvasBackgroundColor ?? ColorRgba.Transparent,
                    Objects = objects
                }
            ],
            Outputs =
            [
                new RenderOutputStateSnapshot
                {
                    Id = outputId,
                    Name = "Offscreen",
                    TypeId = RenderOutputTypes.Offscreen,
                    CanvasId = canvasId,
                    OutputSize = resolvedOutputSize,
                    CanvasLayoutMode = outputCanvasLayoutMode,
                    LetterboxColor = outputLetterboxColor ?? ColorRgba.Black
                }
            ]
        };
    }

    internal static RenderFrameSnapshot CreateCp2Snapshot(
        CanvasId canvasId,
        RenderOutputId outputId,
        FrameSize canvasSize,
        FrameSize outputSize,
        IReadOnlyList<Cp2LayerSpec> layers,
        ColorRgba? canvasBackgroundColor = null,
        ColorRgba? outputLetterboxColor = null,
        LayoutMode outputCanvasLayoutMode = LayoutMode.Fit)
    {
        var objects = layers
            .Select((layer, index) =>
            {
                var frame = new GpuFrameReference
                {
                    Backend = GpuFrameBackend.D3D11SharedTexture,
                    Handle = layer.Handle,
                    TextureSize = layer.Handle.TextureSize,
                    LogicalSize = layer.Handle.TextureSize,
                    SourceId = layer.SourceId,
                    FrameNumber = index + 1
                };

                return (RenderDrawObjectSnapshot)new RenderSourceLayerDrawObjectSnapshot
                {
                    Id = DrawObjectId.New(),
                    Name = $"Layer {index + 1}",
                    Enabled = layer.Enabled,
                    SourceId = layer.SourceId,
                    Transform = layer.Transform,
                    LayoutMode = layer.LayoutMode,
                    Opacity = layer.Opacity,
                    BlendMode = layer.BlendMode,
                    Effects = layer.Effects,
                    BoundFrame = frame
                };
            })
            .ToImmutableArray();

        return new RenderFrameSnapshot
        {
            ProjectStateVersion = 2,
            Canvases =
            [
                new RenderCanvasSnapshot
                {
                    Id = canvasId,
                    Name = "Program",
                    Size = canvasSize,
                    BackgroundColor = canvasBackgroundColor ?? ColorRgba.Transparent,
                    Objects = objects
                }
            ],
            Outputs =
            [
                new RenderOutputStateSnapshot
                {
                    Id = outputId,
                    Name = "Offscreen",
                    TypeId = RenderOutputTypes.Offscreen,
                    CanvasId = canvasId,
                    OutputSize = outputSize,
                    CanvasLayoutMode = outputCanvasLayoutMode,
                    LetterboxColor = outputLetterboxColor ?? ColorRgba.Transparent
                }
            ]
        };
    }

    internal static RenderFrameSnapshot CreateObjectSnapshot(
        CanvasId canvasId,
        RenderOutputId outputId,
        FrameSize canvasSize,
        FrameSize outputSize,
        IReadOnlyList<RenderDrawObjectSnapshot> objects,
        ColorRgba? canvasBackgroundColor = null,
        ColorRgba? outputLetterboxColor = null,
        LayoutMode outputCanvasLayoutMode = LayoutMode.Fit) =>
        new()
        {
            ProjectStateVersion = 3,
            Canvases =
            [
                new RenderCanvasSnapshot
                {
                    Id = canvasId,
                    Name = "Program",
                    Size = canvasSize,
                    BackgroundColor = canvasBackgroundColor ?? ColorRgba.Transparent,
                    Objects = objects.ToImmutableArray()
                }
            ],
            Outputs =
            [
                new RenderOutputStateSnapshot
                {
                    Id = outputId,
                    Name = "Offscreen",
                    TypeId = RenderOutputTypes.Offscreen,
                    CanvasId = canvasId,
                    OutputSize = outputSize,
                    CanvasLayoutMode = outputCanvasLayoutMode,
                    LetterboxColor = outputLetterboxColor ?? ColorRgba.Transparent
                }
            ]
        };

    internal static RenderSourceLayerDrawObjectSnapshot CreateSourceLayer(
        D3D11SharedTextureFrameHandle handle,
        SourceId sourceId,
        Transform2D transform,
        float opacity = 1f,
        LayoutMode layoutMode = LayoutMode.Stretch,
        ColorRgba? letterboxColor = null)
    {
        var frame = new GpuFrameReference
        {
            Backend = GpuFrameBackend.D3D11SharedTexture,
            Handle = handle,
            TextureSize = handle.TextureSize,
            LogicalSize = handle.TextureSize,
            SourceId = sourceId,
            FrameNumber = 1
        };

        return new RenderSourceLayerDrawObjectSnapshot
        {
            Id = DrawObjectId.New(),
            Name = "Source",
            SourceId = sourceId,
            Transform = transform,
            LayoutMode = layoutMode,
            LetterboxColor = letterboxColor ?? ColorRgba.Transparent,
            Opacity = opacity,
            BoundFrame = frame
        };
    }

    internal static RenderCanvasSnapshot CreateSolidCanvas(
        CanvasId canvasId,
        FrameSize size,
        ColorRgba color) =>
        new()
        {
            Id = canvasId,
            Name = "Solid canvas",
            Size = size,
            BackgroundColor = ColorRgba.Transparent,
            Objects =
            [
                new RenderSolidDrawObjectSnapshot
                {
                    Id = DrawObjectId.New(),
                    Name = "Solid",
                    Transform = new Transform2D
                    {
                        Size = new CanvasSize(size.Width, size.Height)
                    },
                    FillColor = color
                }
            ]
        };

    internal static RenderCanvasSnapshot CreateNestedCanvasChain(
        int depth,
        FrameSize size,
        ColorRgba color)
    {
        if (depth < 1)
            throw new ArgumentOutOfRangeException(nameof(depth));

        var current = CreateSolidCanvas(CanvasId.New(), size, color);

        for (var i = 1; i < depth; i++)
        {
            current = new RenderCanvasSnapshot
            {
                Id = CanvasId.New(),
                Name = $"Nested {i}",
                Size = size,
                BackgroundColor = ColorRgba.Transparent,
                Objects =
                [
                    new RenderCanvasDrawObjectSnapshot
                    {
                        Id = DrawObjectId.New(),
                        Name = $"Nested layer {i}",
                        Transform = new Transform2D
                        {
                            Size = new CanvasSize(size.Width, size.Height)
                        },
                        NestedCanvas = current
                    }
                ]
            };
        }

        return current;
    }

    internal static async Task<IReadOnlyList<MediaForgeDiagnostic>?> SubmitDrawObjectForDiagnosticsAsync(
        RenderDrawObjectSnapshot drawObject)
    {
        var diagnostics = new InMemoryDiagnosticsSink();
        if (!TryCreateRenderer(
                out var context,
                diagnostics: diagnostics,
                fontAtlasRasterizer: drawObject is RenderTextDrawObjectSnapshot
                    ? new TestFontAtlasRasterizer()
                    : null))
            return null;

        using (context)
        {
            var guard = context!.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var canvasId = CanvasId.New();
                var size = new FrameSize(64, 64);

                backend.BindOutput(CreateOffscreenBinding(outputId, size.Width, size.Height));

                using var snapshot = new RenderFrameSnapshot
                {
                    ProjectStateVersion = 2,
                    Canvases =
                    [
                        new RenderCanvasSnapshot
                        {
                            Id = canvasId,
                            Name = "Program",
                            Size = size,
                            BackgroundColor = ColorRgba.Transparent,
                            Objects = [drawObject]
                        }
                    ],
                    Outputs =
                    [
                        new RenderOutputStateSnapshot
                        {
                            Id = outputId,
                            Name = "Offscreen",
                            TypeId = RenderOutputTypes.Offscreen,
                            CanvasId = canvasId,
                            OutputSize = size,
                            LetterboxColor = ColorRgba.Transparent
                        }
                    ]
                };

                var submission = backend.Submit(snapshot);
                await ReleaseSubmissionAsync(submission);
            }
            finally
            {
                guard.Clear();
            }
        }

        return diagnostics.Diagnostics;
    }

    public readonly struct Cp2LayerSpec
    {
        public Cp2LayerSpec(
            D3D11SharedTextureFrameHandle handle,
            SourceId sourceId,
            Transform2D transform,
            float opacity = 1f,
            bool enabled = true,
            LayoutMode layoutMode = LayoutMode.Stretch,
            BlendMode blendMode = BlendMode.Normal,
            ImmutableArray<EffectStateSnapshot> effects = default)
        {
            Handle = handle ?? throw new ArgumentNullException(nameof(handle));
            SourceId = sourceId;
            Transform = transform;
            Opacity = opacity;
            Enabled = enabled;
            LayoutMode = layoutMode;
            BlendMode = blendMode;
            Effects = effects.IsDefault ? [] : effects;
        }

        public D3D11SharedTextureFrameHandle Handle { get; }

        public SourceId SourceId { get; }

        public Transform2D Transform { get; }

        public float Opacity { get; }

        public bool Enabled { get; }

        public LayoutMode LayoutMode { get; }

        public BlendMode BlendMode { get; }

        public ImmutableArray<EffectStateSnapshot> Effects { get; }
    }

    internal static Cp2LayerSpec[] CreateFullFrameLayers(
        D3D11SharedTextureFrameHandle bottom,
        D3D11SharedTextureFrameHandle top) =>
        [
            new Cp2LayerSpec(
                bottom,
                SourceId.New(),
                new Transform2D { Size = new CanvasSize(64, 64) }),
            new Cp2LayerSpec(
                top,
                SourceId.New(),
                new Transform2D { Size = new CanvasSize(64, 64) })
        ];

    internal static ChromaKeyEffectSnapshot CreateChromaKeyEffect(
        float similarity,
        float smoothness) =>
        new()
        {
            Id = EffectId.New(),
            Name = "Key green",
            KeyColor = ColorRgba.From(0, 1, 0, 1),
            Similarity = similarity,
            Smoothness = smoothness,
            SpillReduction = 0f
        };

    internal static D3D11SharedTextureFrameHandle CreateFilledSharedTexture(
        D3D11GpuDevice device,
        ColorRgba color)
    {
        var handle = D3D11SharedTextureFactory.CreateSharedTexture(device.Device, width: 64, height: 64);
        FillSharedTexture(device, handle, color);
        return handle;
    }

    internal static void FillSharedTexture(
        D3D11GpuDevice device,
        D3D11SharedTextureFrameHandle handle,
        ColorRgba color)
    {
        handle.KeyedMutex.AcquireSync(handle.ProducerAcquireKey, 1000);

        try
        {
            using var renderTargetView = device.Device.CreateRenderTargetView(handle.Texture);
            device.Context.ClearRenderTargetView(
                renderTargetView,
                new Color4(color.R, color.G, color.B, color.A));
        }
        finally
        {
            handle.KeyedMutex.ReleaseSync(D3D11SharedTextureSyncKeys.Consumer);
            handle.NotifyCaptureReleasedToConsumer();
        }
    }

    internal static void FillSharedTexturePattern(
        D3D11GpuDevice device,
        D3D11SharedTextureFrameHandle handle,
        Func<uint, uint, ColorRgba> colorAt)
    {
        var width = handle.TextureSize.Width;
        var height = handle.TextureSize.Height;
        var rightColor = colorAt(width - 1, 0);
        FillSharedTexture(device, handle, rightColor);

        var splitX = FindVerticalSplit(colorAt, width, height);
        if (splitX == 0 || splitX >= width)
            return;

        var leftColor = colorAt(0, 0);
        if (ColorsEqual(leftColor, rightColor))
            return;

        FillSharedTextureRegion(device, handle, 0, 0, splitX, height, leftColor);
    }

    internal static void FillSharedTextureRegion(
        D3D11GpuDevice device,
        D3D11SharedTextureFrameHandle handle,
        uint dstX,
        uint dstY,
        uint regionWidth,
        uint regionHeight,
        ColorRgba color)
    {
        if (regionWidth == 0 || regionHeight == 0)
            return;

        handle.KeyedMutex.AcquireSync(handle.ProducerAcquireKey, 1000);

        try
        {
            var rowPitch = checked((int)regionWidth * 4);
            var pixels = new byte[checked(rowPitch * (int)regionHeight)];

            for (var y = 0; y < regionHeight; y++)
            {
                for (var x = 0; x < regionWidth; x++)
                {
                    var offset = checked(y * rowPitch + (int)x * 4);
                    pixels[offset] = ToByte(color.B);
                    pixels[offset + 1] = ToByte(color.G);
                    pixels[offset + 2] = ToByte(color.R);
                    pixels[offset + 3] = ToByte(color.A);
                }
            }

            var stagingDescription = new Texture2DDescription
            {
                Width = regionWidth,
                Height = regionHeight,
                MipLevels = 1,
                ArraySize = 1,
                Format = Vortice.DXGI.Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Staging,
                BindFlags = BindFlags.None,
                CPUAccessFlags = CpuAccessFlags.Write
            };

            using var staging = device.Device.CreateTexture2D(stagingDescription);
            var mapped = device.Context.Map(staging, 0, MapMode.Write, Vortice.Direct3D11.MapFlags.None);

            try
            {
                unsafe
                {
                    var destination = new Span<byte>(
                        mapped.DataPointer.ToPointer(),
                        checked((int)(mapped.RowPitch * regionHeight)));
                    for (var y = 0; y < regionHeight; y++)
                    {
                        pixels.AsSpan(y * rowPitch, rowPitch)
                            .CopyTo(destination.Slice(checked(y * (int)mapped.RowPitch), rowPitch));
                    }
                }
            }
            finally
            {
                device.Context.Unmap(staging, 0);
            }

            device.Context.CopySubresourceRegion(
                handle.Texture,
                0,
                dstX,
                dstY,
                0,
                staging,
                0);
        }
        finally
        {
            handle.KeyedMutex.ReleaseSync(D3D11SharedTextureSyncKeys.Consumer);
            handle.NotifyCaptureReleasedToConsumer();
        }
    }

    internal static uint FindVerticalSplit(Func<uint, uint, ColorRgba> colorAt, uint width, uint height)
    {
        if (width <= 1)
            return width;

        var reference = colorAt(0, 0);
        for (var x = 1u; x < width; x++)
        {
            if (!ColorsEqual(colorAt(x, 0), reference) ||
                !ColorsEqual(colorAt(x, height - 1), colorAt(0, height - 1)))
            {
                return x;
            }
        }

        return width;
    }

    internal static bool ColorsEqual(ColorRgba left, ColorRgba right) =>
        ToByte(left.R) == ToByte(right.R) &&
        ToByte(left.G) == ToByte(right.G) &&
        ToByte(left.B) == ToByte(right.B) &&
        ToByte(left.A) == ToByte(right.A);

    internal static byte ToByte(float value) =>
        (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);

    public static void AssertPixelNear(
        VulkanReadbackPixel pixel,
        byte expectedR,
        byte expectedG,
        byte expectedB,
        byte expectedA,
        byte tolerance = 1)
    {
        Assert.InRange((int)pixel.R, expectedR - tolerance, expectedR + tolerance);
        Assert.InRange((int)pixel.G, expectedG - tolerance, expectedG + tolerance);
        Assert.InRange((int)pixel.B, expectedB - tolerance, expectedB + tolerance);
        Assert.InRange((int)pixel.A, expectedA - tolerance, expectedA + tolerance);
    }

    internal static VulkanReadbackPixel ReadPixel(CpuReadbackFrame frame, uint x, uint y)
    {
        if (x >= frame.Size.Width)
            throw new ArgumentOutOfRangeException(nameof(x));

        if (y >= frame.Size.Height)
            throw new ArgumentOutOfRangeException(nameof(y));

        var offset = checked((int)y * frame.StrideBytes + (int)x * 4);
        var pixels = frame.Pixels.Span;
        return new VulkanReadbackPixel(
            pixels[offset],
            pixels[offset + 1],
            pixels[offset + 2],
            pixels[offset + 3]);
    }

    internal static RenderOutputSinkContext CreateSinkContext(
        RenderOutputId outputId,
        FrameSize size) =>
        new(
            outputId,
            size,
            RenderPixelFormat.Rgba8Unorm,
            RenderBackendKind.Vulkan);

    internal static RenderOutputFrameInfo CreateOutputFrameInfo(
        RenderedOutputFrame frame,
        RenderOutputSinkId sinkId,
        long frameNumber = 1) =>
        new(
            frame.OutputId,
            sinkId,
            frameNumber,
            timestamp: TimeSpan.Zero,
            frame.Size,
            frame.Format,
            frame.BackendKind);

    internal static RenderOutputBindingSnapshot CreateOffscreenBinding(
        RenderOutputId outputId,
        uint width,
        uint height) =>
        new()
        {
            OutputId = outputId,
            TargetKind = RenderTargetKind.Offscreen,
            SurfaceSize = new FrameSize(width, height),
            BindingVersion = 1
        };

    internal static RenderFrameSnapshot CreateEmptySnapshot(long version) =>
        new()
        {
            ProjectStateVersion = version,
            Canvases = [],
            Outputs = []
        };

    internal static bool TryCreateSharedTexture(out D3D11GpuDevice device, out D3D11SharedTextureFrameHandle handle)
    {
        device = null!;
        handle = null!;

        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            if (factory.EnumAdapters1(0, out IDXGIAdapter1? adapter).Failure || adapter is null)
            {
                ThrowWhenHardwareIsRequired("No D3D11 adapter was available for the Vulkan composition test.");
                return false;
            }

            device = D3D11GpuDevice.CreateForAdapter(adapter);
            handle = D3D11SharedTextureFactory.CreateSharedTexture(device.Device, width: 64, height: 64);
            return true;
        }
        catch (Exception ex)
        {
            ThrowWhenHardwareIsRequired(
                "D3D11 shared-texture setup failed for the Vulkan composition test.",
                ex);
            return false;
        }
    }

    internal static bool TryCreateCompositionContext(
        out CompositionTestContext? context,
        IVulkanRendererFaultInjector? faultInjector = null,
        IMediaForgeDiagnosticsSink? diagnostics = null,
        IFontAtlasRasterizer? fontAtlasRasterizer = null)
    {
        context = null;

        if (!TryCreateSharedTexture(out var device, out var sharedHandle))
            return false;

        if (!TryCreateRenderer(out var renderer, faultInjector, diagnostics, fontAtlasRasterizer))
        {
            sharedHandle.Dispose();
            device.Dispose();
            return false;
        }

        context = new CompositionTestContext(device, sharedHandle, renderer!, diagnostics);
        return true;
    }

    internal static async Task ReleaseSubmissionAsync(IRenderFrameSubmission submission)
    {
        await submission.WaitForCompletionAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        submission.DisposeCompleted();
    }

    internal static bool TryCreateRenderer(
        out TestRendererContext? context,
        IVulkanRendererFaultInjector? faultInjector = null,
        IMediaForgeDiagnosticsSink? diagnostics = null,
        IFontAtlasRasterizer? fontAtlasRasterizer = null)
    {
        context = null;

        try
        {
            var guard = new RenderThreadGuard();
            if (!MediaForgeVulkanRenderer.TryCreateForLowLevelTests(
                guard,
                diagnostics,
                faultInjector ?? NullVulkanRendererFaultInjector.Instance,
                fontAtlasRasterizer,
                out var backend) ||
                backend is null)
            {
                ThrowWhenHardwareIsRequired("The Vulkan renderer could not be created on the active adapter.");
                return false;
            }

            context = new TestRendererContext(guard, backend);
            return true;
        }
        catch (Exception ex)
        {
            ThrowWhenHardwareIsRequired(
                "The Vulkan renderer failed during hardware test setup.",
                ex);
            return false;
        }
    }

    private static void ThrowWhenHardwareIsRequired(string reason, Exception? innerException = null)
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("WTK_MEDIAFORGE_REQUIRE_HARDWARE_MEDIA"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Required GPU test was not executed: {reason}",
            innerException);
    }

    internal sealed class TestRendererContext : IDisposable
    {
        public TestRendererContext(RenderThreadGuard guard, MediaForgeVulkanRenderer backend)
        {
            Guard = guard;
            Backend = backend;
        }

        public RenderThreadGuard Guard { get; }

        public MediaForgeVulkanRenderer Backend { get; }

        public void Dispose() => Backend.Dispose();
    }

    private sealed class TestFontAtlasRasterizer : IFontAtlasRasterizer
    {
        public FontAtlasAsset Rasterize(string text, string fontFamily, float fontSizePx)
        {
            const int width = 16;
            const int height = 16;
            var pixels = new byte[width * height * 4];

            for (var y = 2; y < 14; y++)
            {
                for (var x = 2; x < 14; x++)
                {
                    var offset = (y * width + x) * 4;
                    pixels[offset] = 255;
                    pixels[offset + 1] = 255;
                    pixels[offset + 2] = 255;
                    pixels[offset + 3] = 255;
                }
            }

            return new FontAtlasAsset
            {
                Text = text,
                FontFamily = fontFamily,
                SizePx = fontSizePx,
                Width = width,
                Height = height,
                AtlasPixels = pixels
            };
        }
    }

    public sealed class CompositionTestContext : IDisposable
    {
        private readonly D3D11GpuDevice _device;
        private readonly D3D11SharedTextureFrameHandle _sharedHandle;
        private readonly TestRendererContext _renderer;

        public CompositionTestContext(
            D3D11GpuDevice device,
            D3D11SharedTextureFrameHandle sharedHandle,
            TestRendererContext renderer,
            IMediaForgeDiagnosticsSink? diagnostics = null)
        {
            _device = device;
            _sharedHandle = sharedHandle;
            _renderer = renderer;
            Diagnostics = diagnostics;
        }

        public IMediaForgeDiagnosticsSink? Diagnostics { get; }

        public D3D11SharedTextureFrameHandle SharedHandle => _sharedHandle;

        public D3D11GpuDevice Device => _device;

        public RenderThreadGuard Guard => _renderer.Guard;

        public MediaForgeVulkanRenderer Backend => _renderer.Backend;

        public void Dispose()
        {
            _renderer.Dispose();
            _sharedHandle.Dispose();
            _device.Dispose();
        }
    }
}
