using Assimp;
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
        public Vector3D<float> _pos;
        public Vector3D<float> _normal;
        //public Silk.NET.Maths.Vector3D<float> _normal;
        public Vector2D<float> _uv;

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
                    Format = Format.R32G32B32Sfloat,
                    Offset = (uint)Marshal.OffsetOf<Vertex>(nameof(_pos))
                },
                new VertexInputAttributeDescription()
                {
                    Binding = 0,
                    Location = 1,
                    Format = Format.R32G32B32Sfloat,
                    Offset = (uint)Marshal.OffsetOf<Vertex>(nameof(_normal)),
                },
                new VertexInputAttributeDescription()
                {
                    Binding = 0,
                    Location = 2,
                    Format = Format.R32G32Sfloat,
                    Offset = (uint)Marshal.OffsetOf<Vertex>(nameof(_uv)),
                }
            };
            return _descriptions;
        }
    }

    internal class AVulkanMesh
    {
        internal Vertex[] _vertices = new Vertex[]
        {
            new Vertex { _pos = new Vector3D<float>(-0.5f, 0.0f,  0.5f), _normal = new Vector3D<float>(0.0f, -1.0f, 0.0f), _uv = new Vector2D<float>(0.0f, 0.0f) },
            new Vertex { _pos = new Vector3D<float>(-0.5f, 0.0f, -0.5f), _normal = new Vector3D<float>(0.0f, -1.0f, 0.0f), _uv = new Vector2D<float>(0.0f, 5.0f) },
            new Vertex { _pos = new Vector3D<float>( 0.5f, 0.0f, -0.5f), _normal = new Vector3D<float>(0.0f, -1.0f, 0.0f), _uv = new Vector2D<float>(5.0f, 5.0f) },
            new Vertex { _pos = new Vector3D<float>( 0.5f, 0.0f,  0.5f), _normal = new Vector3D<float>(0.0f, -1.0f, 0.0f), _uv = new Vector2D<float>(5.0f, 0.0f) },
        
            new Vertex { _pos = new Vector3D<float>(-0.5f, 0.0f,  0.5f), _normal = new Vector3D<float>(-0.8f, 0.5f,  0.0f), _uv = new Vector2D<float>(0.0f, 0.0f) },
            new Vertex { _pos = new Vector3D<float>(-0.5f, 0.0f, -0.5f), _normal = new Vector3D<float>(-0.8f, 0.5f,  0.0f), _uv = new Vector2D<float>(5.0f, 0.0f) },
            new Vertex { _pos = new Vector3D<float>(0.0f, 0.8f,  0.0f), _normal =  new Vector3D<float>(-0.8f, 0.5f,  0.0f), _uv = new Vector2D<float>(2.5f, 5.0f) },
        
            new Vertex { _pos = new Vector3D<float>(-0.5f, 0.0f, -0.5f), _normal = new Vector3D<float>(0.0f, 0.5f, -0.8f), _uv = new Vector2D<float>(5.0f, 0.0f) },
            new Vertex { _pos = new Vector3D<float>(0.5f, 0.0f, -0.5f), _normal =  new Vector3D<float>(0.0f, 0.5f, -0.8f), _uv = new Vector2D<float>(0.0f, 0.0f) },
            new Vertex { _pos = new Vector3D<float>(0.0f, 0.8f,  0.0f), _normal =  new Vector3D<float>(0.0f, 0.5f, -0.8f), _uv = new Vector2D<float>(2.5f, 5.0f) },
            
            new Vertex { _pos = new Vector3D<float>(0.5f, 0.0f, -0.5f), _normal =  new Vector3D<float>(0.8f, 0.5f,  0.0f), _uv = new Vector2D<float>(0.0f, 0.0f) },
            new Vertex { _pos = new Vector3D<float>(0.5f, 0.0f,  0.5f), _normal =  new Vector3D<float>(0.8f, 0.5f,  0.0f), _uv = new Vector2D<float>(5.0f, 0.0f) },
            new Vertex { _pos = new Vector3D<float>(0.0f, 0.8f,  0.0f), _normal =  new Vector3D<float>(0.8f, 0.5f,  0.0f), _uv = new Vector2D<float>(2.5f, 5.0f) },
            
            new Vertex { _pos = new Vector3D<float>(0.5f, 0.0f,  0.5f), _normal =  new Vector3D<float>(0.0f, 0.5f,  0.8f), _uv = new Vector2D<float>(5.0f, 0.0f) },
            new Vertex { _pos = new Vector3D<float>(-0.5f, 0.0f,  0.5f), _normal = new Vector3D<float>(0.0f, 0.5f,  0.8f), _uv = new Vector2D<float>(0.0f, 0.0f) },
            new Vertex { _pos = new Vector3D<float>(0.0f, 0.8f,  0.0f), _normal =  new Vector3D<float>(0.0f, 0.5f,  0.8f), _uv = new Vector2D<float>(2.5f, 5.0f) },
        };

        internal ushort[] _indices = new ushort[]
        {
            0, 1, 2, // Bottom side
	        0, 2, 3, // Bottom side
	        4, 6, 5, // Left side
	        7, 9, 8, // Non-facing side
	        10, 12, 11, // Right side
	        13, 15, 14 // Facing side
        };

        internal void LoadCustomMesh(Scene sc)
        {
            List<Assimp.Vector3D> verts = sc.Meshes[0].Vertices;
            List<Assimp.Vector3D> uvs = sc.Meshes[0].TextureCoordinateChannels[0];
            List<Assimp.Vector3D> normals = sc.Meshes[0].Normals;
            
            _indices = new ushort[sc.Meshes[0].GetIndices().Length];
            for (int i = 0; i < sc.Meshes[0].GetIndices().Length; i++)
            {
                _indices[i] = (ushort)sc.Meshes[0].GetIndices()[i];
            }

            _vertices = new Vertex[sc.Meshes[0].VertexCount];
            for (int i = 0; i < sc.Meshes[0].VertexCount; i++)
            {
                _vertices[0]._pos = new Vector3D<float>(verts[i].X, verts[i].Y,verts[i].Z);
                _vertices[0]._uv = new Vector2D<float>(uvs[i].X, uvs[i].Y);
                _vertices[0]._normal = new Vector3D<float>(normals[i].X, normals[i].Y, normals[i].Z);
            }
        }
    }
}
