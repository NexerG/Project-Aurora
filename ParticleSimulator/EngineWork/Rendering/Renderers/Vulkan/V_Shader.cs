using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArctisAurora.EngineWork.Rendering.Renderers.Vulkan
{
    internal unsafe class V_Shader
    {
        internal List<ShaderCreateInfoEXT> _shaderInfo = new List<ShaderCreateInfoEXT>();
        internal List<ShaderEXT> MakeShaderObjects(string vertex, string fragment, Device _logicalDevice, Vk _vulkan)
        {
            //create shader flags
            ShaderCreateFlagsEXT _flags = ShaderCreateFlagsEXT.LinkStageBitExt;
            ShaderStageFlags _nextStage = ShaderStageFlags.FragmentBit;

            string _vertexCode = ReadFile(ReadFile("../../../../../Shaders/" + vertex));
            string _fragmentCode = ReadFile(ReadFile("../../../../../Shaders/" + fragment));
            ShaderCodeTypeEXT _shaderCodeType = ShaderCodeTypeEXT.SpirvExt;

            fixed (char* _vertexCodePtr = _vertexCode)
            {
                void* voidPtr = _vertexCodePtr;
                ShaderCreateInfoEXT _vertexInfo = new ShaderCreateInfoEXT
                {
                    Flags = _flags,
                    Stage = ShaderStageFlags.VertexBit,
                    NextStage = _nextStage,
                    CodeType = _shaderCodeType,
                    CodeSize = (nuint)(_vertexCode.Length),
                    PCode = voidPtr,
                    PName = (byte*)SilkMarshal.StringToPtr("Default")
                };
                _shaderInfo.Add(_vertexInfo);
            }

            fixed (char* _fragmentCodePtr = _fragmentCode)
            {
                void* voidPtr = _fragmentCodePtr;
                ShaderCreateInfoEXT _fragmentPtr = new ShaderCreateInfoEXT
                {
                    Flags = _flags,
                    Stage = ShaderStageFlags.VertexBit,
                    CodeType = _shaderCodeType,
                    CodeSize = (nuint)(_vertexCode.Length),
                    PCode = voidPtr,
                    PName = (byte*)SilkMarshal.StringToPtr("Default")
                };
                _shaderInfo.Add(_fragmentPtr);
            }
            _vulkan.createshade
        }
        public string ReadFile(string FileName)
        {
            string contents = File.ReadAllText(FileName);
            return contents;
        }

    }
}
