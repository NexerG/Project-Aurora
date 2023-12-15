using OpenTK.Graphics.OpenGL4;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParticleSimulator.EngineWork.Rendering
{
    public class VAO
    {
        int vertex_array_object;
        public VAO()
        {
            vertex_array_object = GL.GenVertexArray();
        }
        public void LinkAttrib(VBO vbo, int layout, int numComponents, VertexAttribPointerType type, int stride, int offset)
        {
            vbo.Bind();
            GL.VertexAttribPointer(layout, numComponents, type, false, stride, offset);
            GL.EnableVertexAttribArray(layout);
            vbo.Unbind();
        }
        public void Bind()
        {
            GL.BindVertexArray(vertex_array_object);
        }
        public void Unbind()
        {
            GL.BindVertexArray(0);
        }
        public void Delete()
        {
            GL.DeleteVertexArray(vertex_array_object);
        }

    }
}
