using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using ParticleSimulator.EngineWork.ECS.RenderingComponents;
using ParticleSimulator.EngineWork.Model;
using ParticleSimulator.GameObject;
using ParticleSimulator.ParticleTypes;
using StbImageSharp;
using static OpenTK.Graphics.OpenGL.GL;

namespace ParticleSimulator.EngineWork.Rendering
{
    public class OpenTK_Renderer : GameWindow
    {
        internal static OpenTK_Renderer _rendererInstance=null;
        //big objects
        private Frame f;
        internal GameWindowSettings _gameWindowSettings;
        internal NativeWindowSettings _nativeWindowSettings;

        private ShaderClass shader;
        public Camera camera = new Camera(new Vector3(0.0f, 0.0f, 2.0f));
        internal List<Entity> _renderQueue = new List<Entity>();

        //shader vars
        uint Texture;
        //particles location, rotation, size matrices

        public OpenTK_Renderer(Frame frame, GameWindowSettings _gws, NativeWindowSettings _nws)
            :base (_gws, _nws)
        {
            _gameWindowSettings = _gws;
            _nativeWindowSettings = _nws;
            //constructor
            f = frame;
            //initialize the stuffs
            _rendererInstance = this;
        }

        protected override void OnLoad()
        {
            base.OnLoad();
            GL.ClearColor(Color.FromArgb(255, 30, 30, 30));

            GL.Enable(EnableCap.DepthTest);
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
            Render(this, null);
            SwapBuffers();
        }

        internal void Prerequisites()
        {
            //initialize the shader pipeline
            shader = new ShaderClass();
        }

        public void Init()
        {
            Run();
        }

        public void Render(object? sender, PaintEventArgs e) //Invalidate function of the 3D renderer
        {
            GL.ClearColor(Color.FromArgb(255, 30, 30, 30));
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            camera.updateMatrix(f);

            Matrix4 matrix = Matrix4.Identity;
            foreach (Entity entity in _renderQueue)
            {
                entity.GetComponent<MeshComponent>().Draw(shader, camera, matrix);
            }
            GL.Finish();
        }

        public void ClearMemory()   //since the libs i use are bindings i assume that i still need to free up memory
        {
            shader.Delete();
            GL.DeleteTextures(1, ref Texture);
            foreach (Entity entity in _renderQueue)
            {
                entity.GetComponent<MeshComponent>().vao.Delete();
                entity.GetComponent<MeshComponent>().vbo.Delete();
                entity.GetComponent<MeshComponent>().ebo.Delete();
                entity.GetComponent<MeshComponent>().ivbo.Delete();
            }
        }
    }
}