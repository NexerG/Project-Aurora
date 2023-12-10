using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.WinForms;
using System.Runtime.InteropServices;
using Windows.UI.Input;

namespace ParticleSimulator.EngineWork
{
    public class OpenTK_Renderer
    {
        private int vertex_array_object;
        private int vertex_buffer_object;
        private int element_buffer_object;
        //private int program;

        private Frame f;
        private ShaderClass shader;
        public OpenTK_Renderer(Frame frame)
        {
            //constructor
            f = frame;
        }

        public void Render(object? sender, PaintEventArgs e)
        {
            float[] vertices =
            {
                //    X                         Y                       Z        R     G     B
                    -0.5f,      (float)(-0.5f * (Math.Sqrt(3)) / 3),    0.0f,   0.8f, 0.3f, 0.02f,
                    0.5f,       (float)(-0.5f * (Math.Sqrt(3)) / 3),    0.0f,   0.8f, 0.3f, 0.02f,
                    0.0f,       (float)(0.5f * (Math.Sqrt(3)) * 2 / 3), 0.0f,   1.0f, 0.6f, 0.32f,
                    -0.5f / 2,  (float)(0.5f * (Math.Sqrt(3)) / 6),     0.0f,   0.9f, 0.45f, 0.17f,
                    0.5f / 2,   (float)(0.5f * (Math.Sqrt(3)) / 6),     0.0f,   0.9f, 0.45f, 0.17f,
                    0.0f,       (float)(-0.5f * (Math.Sqrt(3)) / 3),    0.0f,   0.8f, 0.3f,  0.02f
             };
            //pakuriam eile pagal kuria renderinsim taskus
            uint[] indices =
            {
                0, 3, 5,
                3, 2, 4,
                5, 4, 1
            };

            vertex_array_object = GL.GenVertexArray();
            vertex_buffer_object = GL.GenBuffer();
            element_buffer_object = GL.GenBuffer();

            //VAO
            GL.BindVertexArray(vertex_array_object);
            //VBO
            GL.BindBuffer(BufferTarget.ArrayBuffer, vertex_buffer_object);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);
            //EBO
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, element_buffer_object);
            GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices, BufferUsageHint.StaticDraw);
            //attribute pointers
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 0);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 3 * sizeof(float));
            //enabling those attributes
            GL.EnableVertexAttribArray(0);
            GL.EnableVertexAttribArray(1);

            GL.Viewport(0, 0, f.GLControl.Width, f.GLControl.Height);

            shader = new ShaderClass();
            int uniID = GL.GetUniformLocation(shader.program,"scale");
            GL.Uniform1(uniID, 0.5f);

            UpdateFrame();
        }
        public string ReadFile(string FileName)
        {
            string contents = File.ReadAllText(FileName);
            return contents;
        }

        public void UpdateFrame()
        {
            GL.ClearColor(Color.FromArgb(255, 30, 30, 30));

            //Matrix4 model = Matrix4.Identity;
            //Matrix4 view = Matrix4.CreateTranslation(new Vector3(0.0f, -0.5f, -2.0f));
            //Matrix4 projection = Matrix4.CreatePerspectiveFieldOfView(45f, f.GLControl.AspectRatio,0.1f,100f);

            //int ModelLoc = GL.GetUniformLocation(program, "model");
            //GL.UniformMatrix4(ModelLoc, false, ref model);
            //int ViewLoc = GL.GetUniformLocation(program, "view");
            //GL.UniformMatrix4(ViewLoc, false, ref view);
            //int ProjectionLoc = GL.GetUniformLocation(program, "projection");
            //GL.UniformMatrix4(ProjectionLoc, false, ref projection);

            GL.Enable(EnableCap.DepthTest);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            GL.DrawElements(PrimitiveType.Triangles, 9, DrawElementsType.UnsignedInt, 0);

            f.GLControl.SwapBuffers();
            //f.GLControl.Invalidate();
            Dispose();
        }
        public void Dispose()
        {
            shader.Delete();
            //GL.DeleteProgram(program);
            GL.DeleteBuffer(vertex_buffer_object);
            GL.DeleteVertexArray(vertex_array_object);
            GL.DeleteBuffer(element_buffer_object);
        }
    }
}