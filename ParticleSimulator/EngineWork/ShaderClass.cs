using OpenTK.Compute.OpenCL;
using OpenTK.Graphics.OpenGL4;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParticleSimulator.EngineWork
{
    public class ShaderClass
    {
        public int program;
        public ShaderClass() 
        {
            string VertexCode = ReadFile("../../../Shaders/Default.vert");
            string FragmentCode = ReadFile("../../../Shaders/Default.frag");

            int vertex_shader = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vertex_shader, VertexCode);
            GL.CompileShader(vertex_shader);
            string info_log_vertex = GL.GetShaderInfoLog(vertex_shader);
            if (!string.IsNullOrEmpty(info_log_vertex))
                Console.WriteLine(info_log_vertex);

            int fragment_shader = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragment_shader, FragmentCode);
            GL.CompileShader(fragment_shader);
            string info_log_fragment = GL.GetShaderInfoLog(fragment_shader);
            if (!string.IsNullOrEmpty(info_log_fragment))
                Console.WriteLine(info_log_fragment);

            program = GL.CreateProgram();
            GL.AttachShader(program, vertex_shader);
            GL.AttachShader(program, fragment_shader);
            GL.LinkProgram(program);
            string info_log_program = GL.GetProgramInfoLog(program);
            if (!string.IsNullOrEmpty(info_log_program))
                Console.WriteLine(info_log_program);
            GL.DetachShader(program, vertex_shader);
            GL.DetachShader(program, fragment_shader);
            GL.DeleteShader(vertex_shader);
            GL.DeleteShader(fragment_shader);

            GL.UseProgram(program);
        }
        public string ReadFile(string FileName)
        {
            string contents = File.ReadAllText(FileName);
            return contents;
        }

        public void Delete()
        {
            GL.DeleteProgram(program);
        }
    }
}
