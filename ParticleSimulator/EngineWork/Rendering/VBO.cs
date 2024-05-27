using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArctisAurora.EngineWork.Rendering
{
    public class VBO
    {
        int vertex_buffer_object;

        public VBO(float[] vertices)
        {
            vertex_buffer_object = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, vertex_buffer_object);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);
        }
        public VBO(List<Matrix4> mat4List)
        {
            vertex_buffer_object = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, vertex_buffer_object);
            GL.BufferData(BufferTarget.ArrayBuffer, mat4List.Count * 16 * sizeof(float), mat4List.ToArray(), BufferUsageHint.StaticDraw);
        }
        public void Bind()
        {
            GL.BindBuffer(BufferTarget.ArrayBuffer, vertex_buffer_object);
        }
        public void Unbind()
        {
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        }
        public void Delete()
        {
            GL.DeleteBuffer(vertex_buffer_object);
        }
    }
}
