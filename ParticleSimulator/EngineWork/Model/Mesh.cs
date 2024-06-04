using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using ArctisAurora.EngineWork.Rendering;
using ArctisAurora.ParticleTypes;
using StbImageSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Assimp;

namespace ArctisAurora.EngineWork.Model
{
    public class Vertex
    {
        Vector3 position;
        Vector3 normal;
        Vector3 color;
        Vector2 UV;

        public Vertex(Vector3 pos, Vector3 norm, Vector3 clr, Vector2 uv)
        {
            this.position = pos;
            this.color = clr;
            this.UV = uv;
            this.normal = norm;
        }
    }

    public class Mesh
    {
        //verts and indices
        internal float[] vertices;
        internal uint[] indices;
        internal int[] indc;

        //default
        /*Vertex[] pyramidVertexes =
        {
            new Vertex(new Vector3(-0.5f, 0.0f,  0.5f), new Vector3(0.0f, -1.0f, 0.0f), new Vector3(0.83f, 0.70f, 0.44f) , new Vector2(0.0f, 0.0f)),
            new Vertex(new Vector3(-0.5f, 0.0f, -0.5f), new Vector3(), new Vector3(), new Vector2()),
            new Vertex(new Vector3(0.5f, 0.0f, -0.5f), new Vector3(), new Vector3(), new Vector2()),
            new Vertex(new Vector3(), new Vector3(), new Vector3(), new Vector2()),
            new Vertex(new Vector3(), new Vector3(), new Vector3(), new Vector2()),
            new Vertex(new Vector3(), new Vector3(), new Vector3(), new Vector2()),
            new Vertex(new Vector3(), new Vector3(), new Vector3(), new Vector2()),
        };*/

        float[] pyramidVertices =
        { 
            // X    Y      Z          U     V            NORMALS
            -0.5f, 0.0f,  0.5f,     0.0f, 0.0f,      0.0f, -1.0f, 0.0f, // Bottom side
	        -0.5f, 0.0f, -0.5f,     0.0f, 5.0f,      0.0f, -1.0f, 0.0f, // Bottom side
	         0.5f, 0.0f, -0.5f,     5.0f, 5.0f,      0.0f, -1.0f, 0.0f, // Bottom side
	         0.5f, 0.0f,  0.5f,     5.0f, 0.0f,      0.0f, -1.0f, 0.0f, // Bottom side

	        -0.5f, 0.0f,  0.5f,     0.0f, 0.0f,     -0.8f, 0.5f,  0.0f, // Left Side
	        -0.5f, 0.0f, -0.5f,     5.0f, 0.0f,     -0.8f, 0.5f,  0.0f, // Left Side
	         0.0f, 0.8f,  0.0f,     2.5f, 5.0f,     -0.8f, 0.5f,  0.0f, // Left Side

	        -0.5f, 0.0f, -0.5f,     5.0f, 0.0f,      0.0f, 0.5f, -0.8f, // Non-facing side
	         0.5f, 0.0f, -0.5f,     0.0f, 0.0f,      0.0f, 0.5f, -0.8f, // Non-facing side
	         0.0f, 0.8f,  0.0f,     2.5f, 5.0f,      0.0f, 0.5f, -0.8f, // Non-facing side

	         0.5f, 0.0f, -0.5f,     0.0f, 0.0f,      0.8f, 0.5f,  0.0f, // Right side
	         0.5f, 0.0f,  0.5f,     5.0f, 0.0f,      0.8f, 0.5f,  0.0f, // Right side
	         0.0f, 0.8f,  0.0f,     2.5f, 5.0f,      0.8f, 0.5f,  0.0f, // Right side

	         0.5f, 0.0f,  0.5f,     5.0f, 0.0f,      0.0f, 0.5f,  0.8f, // Facing side
	        -0.5f, 0.0f,  0.5f,     0.0f, 0.0f,      0.0f, 0.5f,  0.8f, // Facing side
	         0.0f, 0.8f,  0.0f,     2.5f, 5.0f,      0.0f, 0.5f,  0.8f  // Facing side
        };

        uint[] pyramidIndices =
        {
            0, 1, 2, // Bottom side
	        0, 2, 3, // Bottom side
	        4, 6, 5, // Left side
	        7, 9, 8, // Non-facing side
	        10, 12, 11, // Right side
	        13, 15, 14 // Facing side
         };

        //textures
        internal Texture textures;
        internal Texture specular;

        public Mesh(float[] vertices, uint[] indices, Texture texture)
        {
            this.vertices = vertices;
            this.indices = indices;
            this.textures = texture;
        }

        public Mesh()
        {
            this.vertices = pyramidVertices;
            this.indices = pyramidIndices;
            textures = new Texture();
            specular = new Texture();
        }
        public Mesh(Texture texture)
        {
            this.vertices = pyramidVertices;
            this.indices = pyramidIndices;
            this.textures = texture;
        }

        internal void LoadMeshFromFile()
        {

        }

        internal void LoadCustomMesh(Scene sc)
        {
            List<Assimp.Vector3D> verts = sc.Meshes[0].Vertices;
            List<Assimp.Vector3D> uvs = sc.Meshes[0].TextureCoordinateChannels[0];
            List<Assimp.Vector3D> normals = sc.Meshes[0].Normals;
            int vertexSize = sc.Meshes[0].VertexCount;
            indices = new uint[sc.Meshes[0].GetIndices().Length];
            for (int i = 0; i < sc.Meshes[0].GetIndices().Length; i++)
            {
                indices[i] = (uint)sc.Meshes[0].GetIndices()[i];
            }

            vertices = new float[verts.Count * 3 + uvs.Count * 2 + normals.Count * 3];
            for (int i = 0; i < verts.Count; i++)
            {
                vertices[i * 8 + 0] = verts[i].X;
                vertices[i * 8 + 1] = verts[i].Y;
                vertices[i * 8 + 2] = verts[i].Z;

                vertices[i * 8 + 3] = uvs[i].X;
                vertices[i * 8 + 4] = uvs[i].Y;

                vertices[i * 8 + 5] = normals[i].X;
                vertices[i * 8 + 6] = normals[i].Y;
                vertices[i * 8 + 7] = normals[i].Z;
            }
        }
    }
}