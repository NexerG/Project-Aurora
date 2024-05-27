using OpenTK.Graphics.OpenGL4;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArctisAurora.EngineWork.Rendering
{
    public class EBO
    {
        public int element_buffer_object;
        public EBO(uint[] indices) 
        {
            element_buffer_object = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, element_buffer_object);
            GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices, BufferUsageHint.StaticDraw);
        }
        public void Bind()
        {
            GL.BindBuffer(BufferTarget.ArrayBuffer, element_buffer_object);
        }
        public void Unbind()
        {
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        }
        public void Delete()
        {
            GL.DeleteBuffer(element_buffer_object);
        }
    }
}
