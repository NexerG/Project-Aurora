using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace ArctisAurora.EngineWork.Rendering.Renderers.Vulkan
{
    internal unsafe class V_Shader
    {
        internal PipelineLayout _pipelineLayout;
        internal List<ShaderCreateInfoEXT> _shaderInfo = new List<ShaderCreateInfoEXT>();
        internal void CreateGraphicsPipeline(string vertex, string fragment, Device _logicalDevice, Vk _vulkan, Extent2D _extent2D, ref RenderPass _renderPass, ref Pipeline _graphicsPipeline, ref DescriptorSetLayout _descriptorSetLayout)
        {
            byte[] _vertexCode = ReadFile("../../../Shaders/" + vertex);
            byte[] _fragmentCode = ReadFile("../../../Shaders/" + fragment);

            ShaderModule _vertexShader = CreateShaderModule(_vertexCode, _vulkan, _logicalDevice);
            ShaderModule _fragmentShader = CreateShaderModule(_fragmentCode, _vulkan, _logicalDevice);

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

            fixed(VertexInputAttributeDescription* _attribDescPtr= _attribDesc)
                fixed(DescriptorSetLayout* _descriptorSetLayoutPtr = &_descriptorSetLayout)
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
                    CullMode = CullModeFlags.BackBit,
                    FrontFace = FrontFace.Clockwise,
                    DepthBiasEnable = false
                };
                PipelineMultisampleStateCreateInfo _multisampling = new PipelineMultisampleStateCreateInfo()
                {
                    SType = StructureType.PipelineMultisampleStateCreateInfo,
                    SampleShadingEnable = false,
                    RasterizationSamples = SampleCountFlags.Count1Bit
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

                if (_vulkan.CreatePipelineLayout(_logicalDevice, _pipelineLayoutInfo, null, out _pipelineLayout) != Result.Success)
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
                    PColorBlendState = &_colorBlending,
                    Layout = _pipelineLayout,
                    RenderPass = _renderPass,
                    Subpass = 0,
                    BasePipelineHandle = default
                };

                Result r = _vulkan.CreateGraphicsPipelines(_logicalDevice, default, 1, _graphicsPipelineInfo, null, out _graphicsPipeline);
                if (r != Result.Success)
                {
                    throw new Exception("Failed to create graphics pipeline " + r);
                }
            }

            _vulkan.DestroyShaderModule(_logicalDevice, _vertexShader, null);
            _vulkan.DestroyShaderModule(_logicalDevice, _fragmentShader, null);
            SilkMarshal.Free((nint)_vertexShaderStageInfo.PName);
            SilkMarshal.Free((nint)_fragmentShaderStageInfo.PName);
        }

        private ShaderModule CreateShaderModule(byte[] _shaderCode, Vk _vulkan, Device _logicalDevice)
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
                if (_vulkan.CreateShaderModule(_logicalDevice, _createInfo, null, out _shaderModule) != Result.Success)
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