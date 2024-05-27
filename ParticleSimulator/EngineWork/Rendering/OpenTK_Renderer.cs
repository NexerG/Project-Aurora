using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using ArctisAurora.EngineWork.ECS.RenderingComponents;
using ArctisAurora.EngineWork.Model;
using ArctisAurora.GameObject;
using ArctisAurora.ParticleTypes;
using StbImageSharp;
using static OpenTK.Graphics.OpenGL.GL;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace ArctisAurora.EngineWork.Rendering
{
    public class OpenTK_Renderer : GameWindow
    {
        internal static OpenTK_Renderer _rendererInstance=null;
        //the form (frame of the engine)
        private Frame f;
        //gamewindow
        internal GameWindowSettings _gameWindowSettings;
        internal NativeWindowSettings _nativeWindowSettings;

        //_entityShader
        internal ShaderClass _entityShader;
        internal ShaderClass _lightSourceShader;
        //camera
        public Camera camera = new Camera();
        internal Vector2 mousePos = new Vector2();
        internal Vector2 prevMousePos = new Vector2();
        internal Vector2 mouseDelta = new Vector2();
        //render queue
        private List<Entity> _renderQueue = new List<Entity>();
        private List<Entity> _lightSourcesRenderQueue = new List<Entity>();

        //_entityShader vars
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
            GL.Viewport(0, 0, ClientSize.X, ClientSize.Y);
        }

        protected override void OnUpdateFrame(FrameEventArgs e)
        {
            base.OnUpdateFrame(e);

            MouseState mouse = MouseState.GetSnapshot();
            mousePos = new Vector2(mouse.X,mouse.Y);
            mouseDelta = mousePos - prevMousePos;
            prevMousePos = mousePos;
            camera.ProcessMouseMovement(mouseDelta);
        }

        protected override void OnRenderFrame(FrameEventArgs e)
        {
            base.OnRenderFrame(e);
        }

        protected override void OnMouseMove(MouseMoveEventArgs e)
        {
            base.OnMouseMove(e);
            
        }

        internal void Prerequisites()
        {
            //initialize the _entityShader pipeline
            _lightSourceShader = new ShaderClass("Light.vert", "Light.frag");
            _entityShader = new ShaderClass("Default.vert", "Default.frag");
        }

        internal void EntityToRenderQueue(Entity e)
        {
            _renderQueue.Add(e);

            _entityShader.Activate();
            e.GetComponent<MeshComponent>().FenceMesh(_entityShader);
        }
        internal void LightToRenderQueue(Entity e)
        {
            _lightSourcesRenderQueue.Add(e);

            _lightSourceShader.Activate();
            e.GetComponent<LightSourceComponent>().FenceMesh(_lightSourceShader);
        }

        public void Init()
        {
            Run();
        }

        public void Render(object? sender, PaintEventArgs e) //Invalidate function of the 3D renderer
        {
            GL.ClearColor(Color.FromArgb(255, 30, 30, 30));
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            camera.updateMatrix();

            //Entity rendering
            _entityShader.Activate();
            foreach (Entity entity in _renderQueue)
            {
                entity.GetComponent<MeshComponent>().Draw(_entityShader, camera);
            }

            //Light source rendering
            _lightSourceShader.Activate();
            foreach(Entity entity in _lightSourcesRenderQueue)
            {
                entity.GetComponent<LightSourceComponent>().Draw(_lightSourceShader, camera);
            }

            GL.Finish();
            SwapBuffers();
        }

        public void ClearMemory()   //since the libs i use are bindings i assume that i still need to free up memory
        {
            _entityShader.Delete();
            GL.DeleteTextures(1, ref Texture);
            foreach (Entity entity in _renderQueue)
            {
                entity.GetComponent<MeshComponent>().vao.Delete();
                entity.GetComponent<MeshComponent>().vbo.Delete();
                entity.GetComponent<MeshComponent>().ebo.Delete();
                entity.GetComponent<MeshComponent>().ivbo.Delete();
            }
            foreach (Entity entity in _lightSourcesRenderQueue)
            {
                entity.GetComponent<MeshComponent>().vao.Delete();
                entity.GetComponent<MeshComponent>().vbo.Delete();
                entity.GetComponent<MeshComponent>().ebo.Delete();
                entity.GetComponent<MeshComponent>().ivbo.Delete();
            }
        }
    }
}