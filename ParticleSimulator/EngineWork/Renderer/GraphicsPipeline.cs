using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace ArctisAurora.EngineWork.Renderer
{
    internal unsafe class GraphicsPipeline
    {
        internal PipelineLayout _pipelineLayout;
        internal Pipeline _graphicsPipeline;
        internal PipelineLayout _shadowLayout;
        internal Pipeline _shadowPipeline;

        internal void CreateGraphicsPipeline(string vertex, string fragment, Extent2D _extent2D, ref DescriptorSetLayout _descriptorSetLayout)
        {
            byte[] _vertexCode = ReadFile("../../../Shaders/" + vertex);
            byte[] _fragmentCode = ReadFile("../../../Shaders/" + fragment);

            ShaderModule _vertexShader = CreateShaderModule(_vertexCode);
            ShaderModule _fragmentShader = CreateShaderModule(_fragmentCode);

            PipelineShaderStageCreateInfo _vertexShaderStageInfo = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit,
                Module = _vertexShader,
                PName = (byte*)SilkMarshal.StringToPtr("main")
            };
            PipelineShaderStageCreateInfo _fragmentShaderStageInfo = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = _fragmentShader,
                PName = (byte*)SilkMarshal.StringToPtr("main")
            };

            var _stages = stackalloc[]
            {
                _vertexShaderStageInfo,
                _fragmentShaderStageInfo
            };

            VertexInputBindingDescription _bindingDesc = Vertex.GetBindingDescription();
            VertexInputAttributeDescription[] _attribDesc = Vertex.GetVertexInputAttributeDescriptions();

            fixed (VertexInputAttributeDescription* _attribDescPtr = _attribDesc)
            fixed (DescriptorSetLayout* _descriptorSetLayoutPtr = &_descriptorSetLayout)
            {
                PipelineVertexInputStateCreateInfo _vertexInputInfo = new PipelineVertexInputStateCreateInfo
                {
                    SType = StructureType.PipelineVertexInputStateCreateInfo,
                    VertexBindingDescriptionCount = 1,
                    VertexAttributeDescriptionCount = (uint)_attribDesc.Length,
                    PVertexBindingDescriptions = &_bindingDesc,
                    PVertexAttributeDescriptions = _attribDescPtr
                };
                PipelineInputAssemblyStateCreateInfo _inputAssembly = new PipelineInputAssemblyStateCreateInfo
                {
                    SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                    Topology = PrimitiveTopology.TriangleList,
                    PrimitiveRestartEnable = false
                };

                Viewport _viewport = new Viewport()
                {
                    X = 0,
                    Y = 0,
                    Width = _extent2D.Width,
                    Height = _extent2D.Height,
                    MinDepth = 0,
                    MaxDepth = 1
                };
                Rect2D _scissor = new Rect2D()
                {
                    Offset = { X = 0, Y = 0 },
                    Extent = _extent2D
                };
                PipelineViewportStateCreateInfo _viewportState = new PipelineViewportStateCreateInfo()
                {
                    SType = StructureType.PipelineViewportStateCreateInfo,
                    ViewportCount = 1,
                    PViewports = &_viewport,
                    ScissorCount = 1,
                    PScissors = &_scissor,
                };
                PipelineRasterizationStateCreateInfo _rasterizer = new PipelineRasterizationStateCreateInfo()
                {
                    SType = StructureType.PipelineRasterizationStateCreateInfo,
                    DepthClampEnable = false,
                    RasterizerDiscardEnable = false,
                    PolygonMode = PolygonMode.Fill,
                    LineWidth = 1,
                    CullMode = CullModeFlags.FrontBit,
                    FrontFace = FrontFace.Clockwise,
                    DepthBiasEnable = false
                };
                PipelineMultisampleStateCreateInfo _multisampling = new PipelineMultisampleStateCreateInfo()
                {
                    SType = StructureType.PipelineMultisampleStateCreateInfo,
                    SampleShadingEnable = false,
                    RasterizationSamples = SampleCountFlags.Count1Bit
                };
                PipelineDepthStencilStateCreateInfo _depthCreateInfo = new PipelineDepthStencilStateCreateInfo()
                {
                    SType = StructureType.PipelineDepthStencilStateCreateInfo,
                    DepthTestEnable = true,
                    DepthWriteEnable = true,
                    DepthCompareOp = CompareOp.Less,
                    DepthBoundsTestEnable = false,
                    StencilTestEnable = false
                };
                PipelineColorBlendAttachmentState _colorBlendAttachment = new PipelineColorBlendAttachmentState()
                {
                    ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit,
                    BlendEnable = false,
                };
                PipelineColorBlendStateCreateInfo _colorBlending = new PipelineColorBlendStateCreateInfo()
                {
                    SType = StructureType.PipelineColorBlendStateCreateInfo,
                    LogicOpEnable = false,
                    LogicOp = LogicOp.Copy,
                    AttachmentCount = 1,
                    PAttachments = &_colorBlendAttachment
                };

                _colorBlending.BlendConstants[0] = 0;
                _colorBlending.BlendConstants[1] = 0;
                _colorBlending.BlendConstants[2] = 0;
                _colorBlending.BlendConstants[3] = 0;

                PipelineLayoutCreateInfo _pipelineLayoutInfo = new PipelineLayoutCreateInfo()
                {
                    SType = StructureType.PipelineLayoutCreateInfo,
                    SetLayoutCount = 1,
                    PushConstantRangeCount = 0,
                    PSetLayouts = _descriptorSetLayoutPtr
                };

                if (VulkanRenderer._vulkan.CreatePipelineLayout(VulkanRenderer._logicalDevice, _pipelineLayoutInfo, null, out _pipelineLayout) != Result.Success)
                {
                    throw new Exception("Failed to create pipeline layout");
                }

                GraphicsPipelineCreateInfo _graphicsPipelineInfo = new GraphicsPipelineCreateInfo()
                {
                    SType = StructureType.GraphicsPipelineCreateInfo,
                    StageCount = 2,
                    PStages = _stages,
                    PVertexInputState = &_vertexInputInfo,
                    PInputAssemblyState = &_inputAssembly,
                    PViewportState = &_viewportState,
                    PRasterizationState = &_rasterizer,
                    PMultisampleState = &_multisampling,
                    PDepthStencilState = &_depthCreateInfo,
                    PColorBlendState = &_colorBlending,
                    Layout = _pipelineLayout,
                    RenderPass = VulkanRenderer._swapchain._renderPass,
                    Subpass = 0,
                    BasePipelineHandle = default
                };

                Result r = VulkanRenderer._vulkan.CreateGraphicsPipelines(VulkanRenderer._logicalDevice, default, 1, _graphicsPipelineInfo, null, out _graphicsPipeline);
                if (r != Result.Success)
                {
                    throw new Exception("Failed to create graphics pipeline " + r);
                }
            }

            VulkanRenderer._vulkan.DestroyShaderModule(VulkanRenderer._logicalDevice, _vertexShader, null);
            VulkanRenderer._vulkan.DestroyShaderModule(VulkanRenderer._logicalDevice, _fragmentShader, null);
            SilkMarshal.Free((nint)_vertexShaderStageInfo.PName);
            SilkMarshal.Free((nint)_fragmentShaderStageInfo.PName);
        }

        internal void CreateShadwomapPipeline(string vertex, Extent2D _shadowTextureSize, ref DescriptorSetLayout _descriptorSetLayout)
        {
            byte[] _vertexCode = ReadFile("../../../Shaders/" + vertex);

            ShaderModule _vertexShader = CreateShaderModule(_vertexCode);

            PipelineShaderStageCreateInfo _vertexShaderStageInfo = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit,
                Module = _vertexShader,
                PName = (byte*)SilkMarshal.StringToPtr("main")
            };
            var _stages = stackalloc[]
            {
                _vertexShaderStageInfo,
            };

            VertexInputBindingDescription _bindingDesc = Vertex.GetBindingDescription();
            VertexInputAttributeDescription[] _attribDesc = Vertex.GetVertexInputAttributeDescriptions();

            fixed (VertexInputAttributeDescription* _attribDescPtr = _attribDesc)
            fixed (DescriptorSetLayout* _descriptorSetLayoutPtr = &_descriptorSetLayout)
            {
                PipelineVertexInputStateCreateInfo _vertexInputInfo = new PipelineVertexInputStateCreateInfo
                {
                    SType = StructureType.PipelineVertexInputStateCreateInfo,
                    VertexBindingDescriptionCount = 1,
                    VertexAttributeDescriptionCount = (uint)_attribDesc.Length,
                    PVertexBindingDescriptions = &_bindingDesc,
                    PVertexAttributeDescriptions = _attribDescPtr
                };
                PipelineInputAssemblyStateCreateInfo _inputAssembly = new PipelineInputAssemblyStateCreateInfo
                {
                    SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                    Topology = PrimitiveTopology.TriangleList,
                    PrimitiveRestartEnable = false
                };
                Viewport _viewport = new Viewport()
                {
                    X = 0,
                    Y = 0,
                    Width = _shadowTextureSize.Width,
                    Height = _shadowTextureSize.Height,
                    MinDepth = 0,
                    MaxDepth = 1
                };
                Rect2D _scissor = new Rect2D()
                {
                    Offset = { X = 0, Y = 0 },
                    Extent = _shadowTextureSize
                };
                PipelineViewportStateCreateInfo _viewportState = new PipelineViewportStateCreateInfo()
                {
                    SType = StructureType.PipelineViewportStateCreateInfo,
                    ViewportCount = 1,
                    PViewports = &_viewport,
                    ScissorCount = 1,
                    PScissors = &_scissor,
                };
                PipelineRasterizationStateCreateInfo _rasterizer = new PipelineRasterizationStateCreateInfo()
                {
                    SType = StructureType.PipelineRasterizationStateCreateInfo,
                    DepthClampEnable = false,
                    RasterizerDiscardEnable = false,
                    PolygonMode = PolygonMode.Fill,
                    LineWidth = 1,
                    CullMode = CullModeFlags.None,
                    DepthBiasEnable = false
                };
                PipelineMultisampleStateCreateInfo _multisampling = new PipelineMultisampleStateCreateInfo()
                {
                    SType = StructureType.PipelineMultisampleStateCreateInfo,
                    SampleShadingEnable = false,
                    RasterizationSamples = SampleCountFlags.Count1Bit
                };
                PipelineDepthStencilStateCreateInfo _depthCreateInfo = new PipelineDepthStencilStateCreateInfo()
                {
                    SType = StructureType.PipelineDepthStencilStateCreateInfo,
                    DepthTestEnable = true,
                    DepthWriteEnable = true,
                    DepthCompareOp = CompareOp.LessOrEqual,
                    DepthBoundsTestEnable = false,
                    StencilTestEnable = false,
                };
                PushConstantRange _pushInfo = new PushConstantRange()
                {
                    StageFlags = ShaderStageFlags.VertexBit,
                    Offset = 0,
                    Size = sizeof(int)
                };
                PipelineLayoutCreateInfo _pipelineLayoutInfo = new PipelineLayoutCreateInfo()
                {
                    SType = StructureType.PipelineLayoutCreateInfo,
                    SetLayoutCount = 1,
                    PushConstantRangeCount = 1,
                    PSetLayouts = _descriptorSetLayoutPtr,
                    PPushConstantRanges = &_pushInfo,
                };

                if (VulkanRenderer._vulkan.CreatePipelineLayout(VulkanRenderer._logicalDevice, _pipelineLayoutInfo, null, out _shadowLayout) != Result.Success)
                {
                    throw new Exception("Failed to create pipeline layout");
                }

                GraphicsPipelineCreateInfo _graphicsPipelineInfo = new GraphicsPipelineCreateInfo()
                {
                    SType = StructureType.GraphicsPipelineCreateInfo,
                    StageCount = 1,
                    PStages = _stages,
                    PVertexInputState = &_vertexInputInfo,
                    PInputAssemblyState = &_inputAssembly,
                    PViewportState = &_viewportState,
                    PRasterizationState = &_rasterizer,
                    PMultisampleState = &_multisampling,
                    PDepthStencilState = &_depthCreateInfo,
                    Layout = _shadowLayout,
                    RenderPass = VulkanRenderer._swapchain._shadowmapRenderPass,
                    Subpass = 0,
                    BasePipelineHandle = default
                };

                Result r = VulkanRenderer._vulkan.CreateGraphicsPipelines(VulkanRenderer._logicalDevice, default, 1, _graphicsPipelineInfo, null, out _shadowPipeline);
                if (r != Result.Success)
                {
                    throw new Exception("Failed to create shadow graphics pipeline " + r);
                }
            }

            VulkanRenderer._vulkan.DestroyShaderModule(VulkanRenderer._logicalDevice, _vertexShader, null);
            SilkMarshal.Free((nint)_vertexShaderStageInfo.PName);
        }

        internal void DestroyPipeline()
        {
            switch (VulkanRenderer._rendererType)
            {
                case ERendererTypes.Rasterizer:
                    {
                        VulkanRenderer._vulkan.DestroyPipeline(VulkanRenderer._logicalDevice, _graphicsPipeline, null);
                        VulkanRenderer._vulkan.DestroyPipelineLayout(VulkanRenderer._logicalDevice, _pipelineLayout, null);
                        VulkanRenderer._vulkan.DestroyPipeline(VulkanRenderer._logicalDevice, _shadowPipeline, null);
                        VulkanRenderer._vulkan.DestroyPipelineLayout(VulkanRenderer._logicalDevice, _shadowLayout, null);
                        break;
                    }
                case ERendererTypes.Pathtracer:
                    {
                        break;
                    }
                case ERendererTypes.RadianceCascades:
                    {
                        break;
                    }
                case ERendererTypes.RadianceCascades2D:
                    {
                        break;
                    }
                default: break;
            }
        }

        private ShaderModule CreateShaderModule(byte[] _shaderCode)
        {
            ShaderModuleCreateInfo _createInfo = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)_shaderCode.Length,
            };
            ShaderModule _shaderModule;

            fixed (byte* _shaderCodePtr = _shaderCode)
            {
                _createInfo.PCode = (uint*)_shaderCodePtr;
                if (VulkanRenderer._vulkan.CreateShaderModule(VulkanRenderer._logicalDevice, ref _createInfo, null, out _shaderModule) != Result.Success)
                {
                    throw new Exception("Failed to create shader module");
                }
            }
            return _shaderModule;
        }

        private byte[] ReadFile(string FileName)
        {
            byte[] contents = File.ReadAllBytes(FileName);
            return contents;
        }
    }
}