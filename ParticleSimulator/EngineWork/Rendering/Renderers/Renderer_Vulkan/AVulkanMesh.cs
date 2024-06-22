using Silk.NET.Maths;
using Silk.NET.Vulkan;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ArctisAurora.EngineWork.Rendering.Renderers.Renderer_Vulkan
{
    struct Vertex
    {
        public Silk.NET.Maths.Vector2D<float> _pos;
        public Silk.NET.Maths.Vector3D<float> _color;
        //public Silk.NET.Maths.Vector3D<float> _normal;
        //public Silk.NET.Maths.Vector2D<float> _uv;

        public static VertexInputBindingDescription GetBindingDescription()
        {
            VertexInputBindingDescription _description = new VertexInputBindingDescription()
            {
                Binding = 0,
                Stride = (uint)Unsafe.SizeOf<Vertex>(),
                InputRate = VertexInputRate.Vertex
            };
            return _description;
        }

        public static VertexInputAttributeDescription[] GetVertexInputAttributeDescriptions()
        {
            VertexInputAttributeDescription[] _descriptions = new VertexInputAttributeDescription[]
            {
                new VertexInputAttributeDescription()
                {
                    Binding = 0,
                    Location = 0,
                    Format = Format.R32G32Sfloat,
                    Offset = (uint)Marshal.OffsetOf<Vertex>(nameof(_pos))
                },
                new VertexInputAttributeDescription()
                {
                    Binding = 0,
                    Location = 1,
                    Format = Format.R32G32Sfloat,
                  Offset = (uint)Marshal.OffsetOf<Vertex>(nameof(_color)),
                }
            };
            return _descriptions;
        }
    }

    internal class AVulkanMesh
    {
        internal Vertex[] _vertices = new Vertex[]
        {
            new Vertex { _pos = new Vector2D<float>(-0.5f,-0.5f), _color = new Vector3D<float>(1.0f, 0.0f, 0.0f) },
            new Vertex { _pos = new Vector2D<float>(0.5f,-0.5f), _color = new Vector3D<float>(0.0f, 1.0f, 0.0f) },
            new Vertex { _pos = new Vector2D<float>(0.5f,0.5f), _color = new Vector3D<float>(0.0f, 0.0f, 1.0f) },
            new Vertex { _pos = new Vector2D<float>(-0.5f,0.5f), _color = new Vector3D<float>(1.0f, 1.0f, 1.0f) },
        };

        internal ushort[] _indices = new ushort[]
        {
            0,1,2,2,3,0
        };
    }
}
