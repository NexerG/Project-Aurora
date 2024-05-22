using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using ParticleSimulator.EngineWork.Model;
using ParticleSimulator.ParticleTypes;
using StbImageSharp;

namespace ParticleSimulator.EngineWork.Rendering
{
    public class OpenTK_Renderer : GameWindow
    {
        //big objects
        private Frame f;
        internal GameWindowSettings _gameWindowSettings;
        internal NativeWindowSettings _nativeWindowSettings;

        private ShaderClass shader;
        public Camera camera = new Camera(new Vector3(0.0f, 0.0f, 2.0f));
        List<Mesh> meshes = new List<Mesh>();

        //shader vars
        uint Texture;
        //particles location, rotation, size matrices
        List<Matrix4> instanceMats = new List<Matrix4>();
        /*public OpenTK_Renderer(Frame frame)
        {
            //constructor
            f = frame;
            //initialize the stuffs
        }*/

        public OpenTK_Renderer(Frame frame, GameWindowSettings _gws, NativeWindowSettings _nws)
            :base (_gws, _nws)
        {
            _gameWindowSettings = _gws;
            _nativeWindowSettings = _nws;
            //constructor
            f = frame;
            //initialize the stuffs
        }

        protected override void OnLoad()
        {
            base.OnLoad();
            GL.ClearColor(Color.FromArgb(255, 30, 30, 30));
            Init3D(f.engine.particles3D);
            //f.engine.Start3D();
        }

        protected override void OnResize(ResizeEventArgs e)
        {
            base.OnResize(e);
            GL.Viewport(0, 0, Size.X, Size.Y);
        }

        protected override void OnUpdateFrame(FrameEventArgs e)
        {
            base.OnUpdateFrame(e);
        }

        protected override void OnRenderFrame(FrameEventArgs e)
        {
            base.OnRenderFrame(e);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            Render(this,null);
            SwapBuffers();
        }

        public async void Init()
        {
            Run();
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

        public void simSetup2D(List<Particle2D> ps)
        {

            for (int i = 0; i < ps.Count; i++)
            {
                Vector3 posTrans = new Vector3(ps[i].point.X, ps[i].point.Y, 0f);
                Quaternion q = new Quaternion(0.0f, 1.0f, 0.0f, 1.0f);
                Vector3 sc = new Vector3(5.0f, 5.0f, 5.0f);

                Matrix4 translation = Matrix4.Identity;
                Matrix4 rotation = Matrix4.Identity;
                Matrix4 scale = Matrix4.Identity;

                Matrix4.CreateTranslation(posTrans, out translation);
                Matrix4.CreateFromQuaternion(q, out rotation);
                Matrix4.CreateScale(sc, out scale);

                Matrix4 tr = Matrix4.Mult(translation, rotation);
                Matrix4 tr_s = Matrix4.Mult(scale, tr);

                instanceMats.Add(tr_s);
            }
            Texture t = new Texture();
            Mesh mesh = new Mesh(vertices, indices, ps.Count, t, ref instanceMats);

            meshes.Add(mesh);
        }
        public void simSetup3D(List<Particle3D> ps)
        {
            for (int i = 0; i < ps.Count; i++)
            {
                Vector3 posTrans = new Vector3(ps[i].point.X, ps[i].point.Y, ps[i].point.Z);
                Quaternion q = new Quaternion(0.0f, 1.0f, 0.0f, 1.0f);
                Vector3 sc = new Vector3(5.0f, 5.0f, 5.0f);

                Matrix4 translation = Matrix4.Identity;
                Matrix4 rotation = Matrix4.Identity;
                Matrix4 scale = Matrix4.Identity;

                Matrix4.CreateTranslation(posTrans, out translation);
                Matrix4.CreateFromQuaternion(q, out rotation);
                Matrix4.CreateScale(sc, out scale);

                Matrix4 tr = Matrix4.Mult(translation, rotation);
                Matrix4 tr_s = Matrix4.Mult(scale, tr);

                instanceMats.Add(tr_s);
            }
            Texture t = new Texture();
            Mesh mesh = new Mesh(vertices, indices, ps.Count, t, ref instanceMats);

            meshes.Add(mesh);
        }

        public void Init3D(List<Particle3D> ps)
        {
            //create shaders
            shader = new ShaderClass();
            simSetup3D(ps);

            GL.Enable(EnableCap.DepthTest);
        }

        public void UpdatePositions3D(List<Particle3D> ps)
        {
            for (int i = 0; i < ps.Count; i++)
            {
                Vector3 posTrans = new Vector3(-ps[i].point.X, -ps[i].point.Y, ps[i].point.Z);
                Quaternion q = new Quaternion(0.0f, 1.0f, 0.0f, 1.0f);
                Vector3 sc = new Vector3(5.0f, 5.0f, 5.0f);

                Matrix4 translation = Matrix4.Identity;
                Matrix4 rotation = Matrix4.Identity;
                Matrix4 scale = Matrix4.Identity;

                Matrix4.CreateTranslation(posTrans, out translation);
                Matrix4.CreateFromQuaternion(q, out rotation);
                Matrix4.CreateScale(sc, out scale);

                Matrix4 tr = Matrix4.Mult(translation, rotation);
                Matrix4 tr_s = Matrix4.Mult(scale, tr);

                instanceMats[i] = tr_s;
            }
            foreach (Mesh m in meshes)
            {
                m.updateMatrices(instanceMats);
            }
        }

        public void Render(object? sender, PaintEventArgs e) //Invalidate function of the 3D renderer
        {
            GL.ClearColor(Color.FromArgb(255, 30, 30, 30));
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            camera.updateMatrix(f);

            Matrix4 matrix = Matrix4.Identity;
            foreach(Mesh m in meshes)
            {
                m.Draw(shader, camera, matrix);
            }

            GL.Finish();
        }

        public void ClearMemory()   //since the libs i use are bindings i assume that i still need to free up memory
        {
            shader.Delete();
            GL.DeleteTextures(1, ref Texture);
            foreach (Mesh m in meshes)
            {
                m.vao.Delete();
                m.vbo.Delete();
                m.ebo.Delete();
                m.ivbo.Delete();
            }
        }
    }
}