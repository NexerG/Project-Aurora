using OpenTK.Graphics.OpenGL4;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArctisAurora.EngineWork.Rendering.Renderers.OpenTK
{
    public class EBO
    {
        public int element_buffer_object;
        public EBO()
        {
            element_buffer_object = GL.GenBuffer();
        }

        internal void BufferElementData(uint[] indices)
        {
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, element_buffer_object);
            GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices, BufferUsageHint.StaticDraw);
            Unbind();
        }

        internal void Bind()
        {
            GL.BindBuffer(BufferTarget.ArrayBuffer, element_buffer_object);
        }
        internal void Unbind()
        {
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        }
        internal void Delete()
        {
            GL.DeleteBuffer(element_buffer_object);
        }
    }
}
