using ArctisAurora.EngineWork.Renderer.Helpers;
using Assimp;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace ArctisAurora.EngineWork.Renderer
{
    struct Vertex
    {
        public Vector3D<float> _pos;
        public Vector3D<float> _normal;
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
            VertexInputAttributeDescription[] _descriptions = new[]
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
        internal Buffer _vertexBuffer;
        internal DeviceMemory _vertexBufferMemory;

        internal Buffer _indexBuffer;
        internal DeviceMemory _indexBufferMemory;

        /*internal Vertex[] _vertices = new[]
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
        internal uint[] _indices = new uint[]
{
            0, 1, 2, // Bottom side
	        0, 2, 3, // Bottom side
	        4, 6, 5, // Left side
	        7, 9, 8, // Non-facing side
	        10, 12, 11, // Right side
	        13, 15, 14 // Facing side
        };*/

        internal void BufferMesh()
        {
            AVulkanBufferHandler.CreateBuffer(ref _vertices, ref _vertexBuffer, ref _vertexBufferMemory, AVulkanBufferHandler.vertexBufferFlags);
            AVulkanBufferHandler.CreateBuffer(ref _indices, ref _indexBuffer, ref _indexBufferMemory, AVulkanBufferHandler.indexBufferFlags);
        }

        internal Vertex[] _vertices;
        internal uint[] _indices;

        internal unsafe void LoadCustomMesh(Scene sc)
        {
            VulkanRenderer._vulkan.DestroyBuffer(VulkanRenderer._logicalDevice, _vertexBuffer, null);
            VulkanRenderer._vulkan.DestroyBuffer(VulkanRenderer._logicalDevice, _indexBuffer, null);
            VulkanRenderer._vulkan.FreeMemory(VulkanRenderer._logicalDevice, _indexBufferMemory, null);
            VulkanRenderer._vulkan.FreeMemory(VulkanRenderer._logicalDevice, _vertexBufferMemory, null);

            List<Assimp.Vector3D> verts = sc.Meshes[0].Vertices;
            List<Assimp.Vector3D> uvs = sc.Meshes[0].TextureCoordinateChannels[0];
            List<Assimp.Vector3D> normals = sc.Meshes[0].Normals;

            _indices = new uint[sc.Meshes[0].GetIndices().Length];
            for (int i = 0; i < sc.Meshes[0].GetIndices().Length; i++)
            {
                _indices[i] = (uint)sc.Meshes[0].GetIndices()[i];
            }

            _vertices = new Vertex[sc.Meshes[0].VertexCount];
            for (int i = 0; i < sc.Meshes[0].VertexCount; i++)
            {
                _vertices[i]._pos = new Vector3D<float>(verts[i].X, verts[i].Y, verts[i].Z);
                _vertices[i]._uv = new Vector2D<float>(uvs[i].X, uvs[i].Y);
                _vertices[i]._normal = new Vector3D<float>(normals[i].X, normals[i].Y, normals[i].Z);
            }

            AVulkanBufferHandler.CreateBuffer(ref _vertices, ref _vertexBuffer, ref _vertexBufferMemory, AVulkanBufferHandler.vertexBufferFlags);
            AVulkanBufferHandler.CreateBuffer(ref _indices, ref _indexBuffer, ref _indexBufferMemory, AVulkanBufferHandler.indexBufferFlags);
        }

        internal unsafe void UpdateBuffers()
        {
            AVulkanBufferHandler.UpdateBuffer(ref _vertices, ref _vertexBuffer, ref _vertexBufferMemory, AVulkanBufferHandler.vertexBufferFlags);
            AVulkanBufferHandler.UpdateBuffer(ref _indices, ref _indexBuffer, ref _indexBufferMemory, AVulkanBufferHandler.indexBufferFlags);
        }

        internal static unsafe AVulkanMesh LoadDefault()
        {
            AVulkanMesh mesh = new AVulkanMesh();
            mesh._vertices = new[]
            {
                // bottom.
                new Vertex { _pos = new Vector3D<float>(-0.5f, 0.0f,  0.5f), _normal = new Vector3D<float>(0.0f, -1.0f, 0.0f), _uv = new Vector2D<float>(0.0f, 0.0f) },
                new Vertex { _pos = new Vector3D<float>(-0.5f, 0.0f, -0.5f), _normal = new Vector3D<float>(0.0f, -1.0f, 0.0f), _uv = new Vector2D<float>(0.0f, 5.0f) },
                new Vertex { _pos = new Vector3D<float>( 0.5f, 0.0f, -0.5f), _normal = new Vector3D<float>(0.0f, -1.0f, 0.0f), _uv = new Vector2D<float>(5.0f, 5.0f) },
                new Vertex { _pos = new Vector3D<float>( 0.5f, 0.0f,  0.5f), _normal = new Vector3D<float>(0.0f, -1.0f, 0.0f), _uv = new Vector2D<float>(5.0f, 0.0f) },
                // pyramid top.
                new Vertex { _pos = new Vector3D<float>(0.0f, 0.8f,  0.0f), _normal =  new Vector3D<float>(0.0f, 0.5f,  0.8f), _uv = new Vector2D<float>(2.5f, 5.0f) },
            };
            mesh._indices = new uint[]
            {
                0, 1, 2, // Bottom side
	            0, 2, 3, // Bottom side
	            4, 1, 0, // Left side
	            4, 2, 1, // Non-facing side
	            4, 3, 2, // Right side
	            4, 0, 3  // Facing side
            };
            mesh.BufferMesh();

            return mesh;
        }

        internal void RecalculateNormals()
        {

        }
    }
}