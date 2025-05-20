using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ArctisAurora.EngineWork.Renderer.Helpers;
using ArctisAurora.EngineWork.Renderer;
using Silk.NET.Vulkan;
using Image = Silk.NET.Vulkan.Image;
using ImageLayout = Silk.NET.Vulkan.ImageLayout;
using ArctisAurora.GameObject;

namespace ArctisAurora.CustomEntities
{
    internal unsafe class Layer : Entity
    {
        internal Image layerImage;
        internal ImageView layerImageView;
        internal DeviceMemory layerImageDM;

        internal Image layerLightsImage;
        internal ImageView layerLightsView;
        internal DeviceMemory layerLightsDM;

        internal Image layerFImage;
        internal ImageView layerFView;
        internal DeviceMemory layerFDM;

        internal Layer()
        {
            layerImage = new Image();
            layerImageView = new ImageView();
            layerImageDM = new DeviceMemory();
            CreateImage(ref VulkanRenderer._extent, ref layerImage, ref layerImageDM, ref layerImageView, Format.R8G8B8A8Unorm);

            layerLightsImage = new Image();
            layerLightsView = new ImageView();
            layerLightsDM = new DeviceMemory();
            CreateImage(ref VulkanRenderer._extent, ref layerLightsImage, ref layerLightsDM, ref layerLightsView, Format.R8G8B8A8Unorm);

            layerFImage = new Image();
            layerFView = new ImageView();
            layerFDM = new DeviceMemory();
            CreateImage(ref VulkanRenderer._extent, ref layerFImage, ref layerFDM, ref layerFView, Format.R8G8B8A8Unorm);

            //VulkanRenderer._rendererInstance.AddEntityToRenderQueue(this);
        }

        private void CreateImage(ref Extent2D size, ref Image image, ref DeviceMemory deviceMemory, ref ImageView imageView, Format format)
        {
            AVulkanBufferHandler.CreateImage(size.Width, size.Height, format, ImageTiling.Optimal, ImageUsageFlags.TransferSrcBit | ImageUsageFlags.StorageBit | ImageUsageFlags.TransferDstBit, MemoryPropertyFlags.DeviceLocalBit, ref image, ref deviceMemory);
            VulkanRenderer._swapchain.CreateImageView(ref imageView, ref image, ImageAspectFlags.ColorBit, format);

            CommandBuffer _imageTransition = AVulkanBufferHandler.BeginSingleTimeCommands();

            ImageMemoryBarrier _barrier = new()
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = ImageLayout.Undefined,
                NewLayout = ImageLayout.General,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = image,
                SubresourceRange =
                    {
                    AspectMask = ImageAspectFlags.ColorBit,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1,
                    },
                SrcAccessMask = 0,
                DstAccessMask = AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
            };
            VulkanRenderer._vulkan.CmdPipelineBarrier(_imageTransition, PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.ComputeShaderBit, 0, 0, null, 0, null, 1, ref _barrier);
            AVulkanBufferHandler.EndSingleTimeCommands(ref _imageTransition);
        }
    }
}
