﻿using OpenTK.Graphics.OpenGL4;

namespace ArctisAurora.EngineWork.Rendering.Renderers.OpenTK
{
    //a class responsible for computing shaders. that includes vertex shaders, shadows (fragment), and textures
    public class ShaderClass
    {
        public enum entityShaderType
        {
            lightsource,
            entity,
            grass,
            water
        }

        public int program;
        internal entityShaderType type;
        public ShaderClass(string vert, string frag, entityShaderType type)
        {
            this.type = type;
            string VertexCode = ReadFile("../../../Shaders/" + vert);
            string FragmentCode = ReadFile("../../../Shaders/" + frag);
            //create shaders
            int vertex_shader = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vertex_shader, VertexCode);
            GL.CompileShader(vertex_shader);
            string info_log_vertex = GL.GetShaderInfoLog(vertex_shader);
            if (!string.IsNullOrEmpty(info_log_vertex))
                Console.WriteLine(info_log_vertex);
            //create lights
            int fragment_shader = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragment_shader, FragmentCode);
            GL.CompileShader(fragment_shader);
            string info_log_fragment = GL.GetShaderInfoLog(fragment_shader);
            if (!string.IsNullOrEmpty(info_log_fragment))
                Console.WriteLine(info_log_fragment);
            //compute shaders and lights
            program = GL.CreateProgram();
            GL.AttachShader(program, vertex_shader);
            GL.AttachShader(program, fragment_shader);
            GL.LinkProgram(program);
            string info_log_program = GL.GetProgramInfoLog(program);
            if (!string.IsNullOrEmpty(info_log_program))
                Console.WriteLine(info_log_program);

            //free memory
            GL.DetachShader(program, vertex_shader);
            GL.DetachShader(program, fragment_shader);
            GL.DeleteShader(vertex_shader);
            GL.DeleteShader(fragment_shader);
            //A way for openGL to decide what shaders to use in order to calculate the objects
            Activate();
        }
        public string ReadFile(string FileName)
        {
            string contents = File.ReadAllText(FileName);
            return contents;
        }

        public void Activate()
        {
            GL.UseProgram(program);
        }

        public void Delete()
        {
            GL.DeleteProgram(program);
        }
    }
}
