using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using ParticleSimulator.EngineWork.Rendering;
using ParticleSimulator.ParticleTypes;
using StbImageSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace ParticleSimulator.EngineWork.Model
{
    public class Mesh
    {
        //verts and indices
        internal float[] vertices;
        internal uint[] indices;

        //default
        float[] pyramidVertices =
        {
            // X    Y      Z          R     G     B          U     V
	        -0.5f, 0.0f,  0.5f,     0.83f, 0.70f, 0.44f,    0.0f, 0.0f,
            -0.5f, 0.0f, -0.5f,     0.83f, 0.70f, 0.44f,    5.0f, 0.0f,
             0.5f, 0.0f, -0.5f,     0.83f, 0.70f, 0.44f,    0.0f, 0.0f,
             0.5f, 0.0f,  0.5f,     0.83f, 0.70f, 0.44f,    5.0f, 0.0f,
             0.0f, 0.8f,  0.0f,     0.92f, 0.86f, 0.76f,    2.5f, 5.0f
        };
        uint[] pyramidIndices =
        {
            0, 1, 2,
            0, 2, 3,
            0, 1, 4,
            1, 2, 4,
            2, 3, 4,
            3, 0, 4
        };

        //textures
        internal Texture textures;

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
    }
}
