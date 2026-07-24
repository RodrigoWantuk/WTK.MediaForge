using Silk.NET.Vulkan;
using WTK.MediaForge.Graphics.Vulkan.Rendering;
using WTK.MediaForge.Testing;
using Xunit;

namespace WTK.MediaForge.Graphics.Vulkan.Tests;

[Trait("Category", TestCategories.Gpu)]
public sealed class VulkanCommandPoolSynchronizationTests
{
    [Fact]
    public async Task Primary_and_auxiliary_command_pools_support_concurrent_recording()
    {
        using var device = VulkanHeadlessDevice.Create();
        const int iterationsPerWorker = 500;
        using var startBarrier = new Barrier(2);

        var renderWorker = Task.Factory.StartNew(() =>
        {
            startBarrier.SignalAndWait();
            for (var iteration = 0; iteration < iterationsPerWorker; iteration++)
            {
                var commandBuffer = device.AllocateAndBeginPrimaryCommandBuffer(
                    "parallel render recording test");
                try
                {
                    Assert.Equal(Result.Success, device.Vk.EndCommandBuffer(commandBuffer));
                }
                finally
                {
                    device.FreePrimaryCommandBuffer(commandBuffer);
                }
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        var exportWorker = Task.Factory.StartNew(() =>
        {
            startBarrier.SignalAndWait();
            for (var iteration = 0; iteration < iterationsPerWorker; iteration++)
            {
                lock (device.AuxiliaryCommandPoolGate)
                {
                    var commandBuffer = device.AllocateAndBeginAuxiliaryCommandBuffer(
                        "parallel export recording test");
                    try
                    {
                        Assert.Equal(Result.Success, device.Vk.EndCommandBuffer(commandBuffer));
                    }
                    finally
                    {
                        device.FreeAuxiliaryCommandBuffer(commandBuffer);
                    }
                }
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        await Task.WhenAll(renderWorker, exportWorker);
    }
}
