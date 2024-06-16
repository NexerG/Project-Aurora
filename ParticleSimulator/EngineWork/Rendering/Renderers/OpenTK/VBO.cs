using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArctisAurora.EngineWork.Rendering.Renderers.OpenTK
{
    public class VBO
    {
        int vertex_buffer_object;

        public VBO()
        {
            vertex_buffer_object = GL.GenBuffer();
        }

        internal void BufferMatrixData(List<Matrix4> instanceMatrixes)
        {
            Bind();
            GL.BufferData(BufferTarget.ArrayBuffer, instanceMatrixes.Count * 16 * sizeof(float), instanceMatrixes.ToArray(), BufferUsageHint.StaticDraw);
            Unbind();
        }

        internal void BufferVertexData(float[] vertexes)
        {
            Bind();
            GL.BufferData(BufferTarget.ArrayBuffer, vertexes.Length * sizeof(float), vertexes, BufferUsageHint.StaticDraw);
            Unbind();
        }

        internal void Bind()
        {
            GL.BindBuffer(BufferTarget.ArrayBuffer, vertex_buffer_object);
        }
        internal void Unbind()
        {
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        }
        internal void Delete()
        {
            GL.DeleteBuffer(vertex_buffer_object);
        }
    }
}
