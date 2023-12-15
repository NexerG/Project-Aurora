using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using ParticleSimulator.EngineWork.Model;
using StbImageSharp;

namespace ParticleSimulator.EngineWork.Rendering
{
    public class OpenTK_Renderer
    {
        //big objects
        private Frame f;
        private ShaderClass shader;
        public Camera camera = new Camera(new Vector3(0.0f, 0.0f, 2.0f));
        List<Mesh> meshes = new List<Mesh>();

        //shader vars
        uint Texture;

        public OpenTK_Renderer(Frame frame)
        {
            //constructor
            f = frame;
            //initialize the stuffs
            Init();
        }

        float[] vertices =
        {
            // X    Y      Z          R     G     B          U     V
	        -0.5f, 0.0f,  0.5f,     0.83f, 0.70f, 0.44f,    0.0f, 0.0f,
            -0.5f, 0.0f, -0.5f,     0.83f, 0.70f, 0.44f,    5.0f, 0.0f,
             0.5f, 0.0f, -0.5f,     0.83f, 0.70f, 0.44f,    0.0f, 0.0f,
             0.5f, 0.0f,  0.5f,     0.83f, 0.70f, 0.44f,    5.0f, 0.0f,
             0.0f, 0.8f,  0.0f,     0.92f, 0.86f, 0.76f,    2.5f, 5.0f
        };
        List<float[]> objs = new List<float[]>();
        int count = 2;

        //pakuriam eile pagal kuria renderinsim taskus
        uint[] indices =
        {
            0, 1, 2,
            0, 2, 3,
            0, 1, 4,
            1, 2, 4,
            2, 3, 4,
            3, 0, 4
        };

        public void Init()
        {
            //updating viewport to fill the area
            GL.Viewport(0, 0, f.GLControl.Width, f.GLControl.Height);
            //create shaders
            shader = new ShaderClass();

            GL.Enable(EnableCap.DepthTest);

            Texture t = new Texture();

            int pyramids = 5;
            List<Matrix4> instanceMats = new List<Matrix4>();
            for (int i = 0; i < pyramids; i++)
            {
                Vector3 Trans = new Vector3(0f, i * 2f, 0f);
                Quaternion q = new Quaternion(0.0f, 1.0f, 0.0f, 1.0f);
                //Vector3 sc = new Vector3(1.0f,1.0f,1.0f);

                Matrix4 translation = Matrix4.Identity;
                Matrix4 rotation = Matrix4.Identity;
                //Matrix4 scale = Matrix4.Identity;

                Matrix4.CreateTranslation(Trans, out translation);
                Matrix4.CreateFromQuaternion(q, out rotation);
                //Matrix4.CreateScale(sc, out scale);

                Matrix4 tr = Matrix4.Mult(translation,rotation);
                instanceMats.Add(tr);
            }
            Mesh mesh = new Mesh(vertices, indices, pyramids, t, instanceMats);

            meshes.Add(mesh);
        }

        public void Render(object? sender, PaintEventArgs e) //Invalidate function of the 3D renderer
        {
            GL.ClearColor(Color.FromArgb(255, 30, 30, 30));
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            camera.updateMatrix(f);
            Matrix4 mat = Matrix4.Identity;
            meshes[0].Draw(
                shader,
                camera,
                mat,
                new Vector3(0, 0, 0),
                new Quaternion(1, 0, 0, 0),
                new Vector3(1, 1, 1)
                );
            f.GLControl.SwapBuffers();
            //ClearMemory();
        }

        public void ClearMemory()   //since the libs i use are bindings i assume that i still need to free up memory
        {
            shader.Delete();
            GL.DeleteTextures(1, ref Texture);
            foreach(Mesh m in meshes)
            {
                m.vao.Delete();
                m.vbo.Delete();
                m.ebo.Delete();
                m.ivbo.Delete();
            }
        }
    }
}