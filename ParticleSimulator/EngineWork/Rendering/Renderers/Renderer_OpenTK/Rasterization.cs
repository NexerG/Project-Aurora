using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using ArctisAurora.EngineWork.Model;
using ArctisAurora.GameObject;
using ArctisAurora.ParticleTypes;
using StbImageSharp;
using static OpenTK.Graphics.OpenGL.GL;
using OpenTK.Windowing.GraphicsLibraryFramework;
using static ArctisAurora.EngineWork.Rendering.Renderers.OpenTK.ShaderClass;
using Keys = OpenTK.Windowing.GraphicsLibraryFramework.Keys;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using System.Runtime.InteropServices;
using ArctisAurora.EngineWork.ECS.RenderingComponents.OpenTK;

namespace ArctisAurora.EngineWork.Rendering.Renderers.OpenTK
{
    internal class Rasterization : GameWindow
    {
        //testing
        int ssbo;

        internal static Rasterization _rendererInstance = null;
        //gamewindow
        internal GameWindowSettings _gameWindowSettings;
        internal NativeWindowSettings _nativeWindowSettings;

        //shaders
        internal ShaderClass _entityShader;
        internal ShaderClass _lightSourceShader;
        internal Dictionary<entityShaderType, ShaderClass> shaderPrograms = new Dictionary<entityShaderType, ShaderClass>();
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

        public Rasterization(GameWindowSettings _gws, NativeWindowSettings _nws)
            : base(_gws, _nws)
        {
            _gameWindowSettings = _gws;
            _nativeWindowSettings = _nws;
            _rendererInstance = this;
        }

        protected override void OnLoad()
        {
            base.OnLoad();
            GL.ClearColor(Color.FromArgb(255, 30, 30, 30));

            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.CullFace);
            GL.CullFace(CullFaceMode.Back);
            GL.FrontFace(FrontFaceDirection.Ccw);
        }

        protected override void OnResize(ResizeEventArgs e)
        {
            base.OnResize(e);
            GL.Viewport(0, 0, ClientSize.X, ClientSize.Y);
        }

        protected override void OnUpdateFrame(FrameEventArgs e)
        {
            base.OnUpdateFrame(e);

            HandleMouse();
            HandleKeyboard();
        }

        protected override void OnRenderFrame(FrameEventArgs e)
        {
            base.OnRenderFrame(e);
        }

        protected override void OnMouseMove(MouseMoveEventArgs e)
        {
            base.OnMouseMove(e);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct DebugData
        {
            public float x, y, z;
        }

        internal void Prerequisites()
        {
            //initialize the entity & light source shaders
            _lightSourceShader = new ShaderClass("Light.vert", "Light.frag", entityShaderType.lightsource);
            shaderPrograms.Add(entityShaderType.lightsource, _lightSourceShader);

            _entityShader = new ShaderClass("Default.vert", "Default.frag", entityShaderType.entity);
            shaderPrograms.Add(entityShaderType.entity, _entityShader);

            //debug
            GL.GenBuffers(2, out ssbo);
            GL.BindBuffer(BufferTarget.ShaderStorageBuffer, ssbo);
            GL.BufferData(BufferTarget.ShaderStorageBuffer, sizeof(float) * 3, nint.Zero, BufferUsageHint.DynamicCopy);
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 0, ssbo); // Binding point 0
            GL.BindBuffer(BufferTarget.ShaderStorageBuffer, 0);
        }

        internal void HandleMouse()
        {
            if (IsFocused)
            {
                MouseState mouse = MouseState.GetSnapshot();
                mouseDelta = mouse.Position - mouse.PreviousPosition;
                camera.ProcessMouseMovement(mouseDelta);
                CursorState = CursorState.Grabbed;
            }
            else CursorState = CursorState.Normal;
        }

        internal void HandleKeyboard()
        {
            if (IsFocused)
            {
                KeyboardState keyboard = KeyboardState.GetSnapshot();
                camera.ProcessKeyboard(keyboard);
            }
        }

        internal void CreateEntityShader(string vertShader, string fragShader, entityShaderType t)
        {
            if (!shaderPrograms.ContainsKey(t))
            {
                ShaderClass shader = new ShaderClass(vertShader, fragShader, t);
                shaderPrograms.Add(t, shader);
            }
        }

        internal void ChangeShader(entityShaderType t)
        {
            shaderPrograms.TryGetValue(t, out var shader);
            if (shader != null) shader.Activate();
        }

        internal void EntityToRenderQueue(Entity e)
        {
            _renderQueue.Add(e);
        }

        internal void LightToRenderQueue(Entity e)
        {
            _lightSourcesRenderQueue.Add(e);
        }

        internal List<Entity> GetLightSources()
        {
            return _lightSourcesRenderQueue;
        }

        internal void setupLights(ShaderClass shader)
        {
            GL.Uniform4(GL.GetUniformLocation(shader.program, "lightColor"), 1f, 1f, 1f, 1f);
            //foreach (Entity e in _lightSourcesRenderQueue)
            {
                //GL.Uniform3(GL.GetUniformLocation(shader.program, "lightPos"), ref _lightSourcesRenderQueue[0].transform.position);
            }
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

            //--------------------------------------
            //rendering
            //Entity rendering
            shaderPrograms.TryGetValue(entityShaderType.entity, out var shader);
            if (shader != null)
            {
                shader.Activate();
                setupLights(shader);
                GL.Uniform3(GL.GetUniformLocation(shader.program, "camPos"), camera.pos);
                GL.UniformMatrix4(GL.GetUniformLocation(shader.program, "camMatrix"), false, ref camera.pv);
            }

            foreach (Entity entity in _renderQueue)
            {
                entity.GetComponent<MeshComponent_OpenTK>().Draw(_entityShader);
            }

            //Light source rendering
            {
                shaderPrograms.TryGetValue(entityShaderType.lightsource, out var shader2);
                if (shader2 != null) shader2.Activate();
                GL.UniformMatrix4(GL.GetUniformLocation(shader2.program, "camMatrix"), false, ref camera.pv);
            }
            foreach (Entity entity in _lightSourcesRenderQueue)
            {
                entity.GetComponent<LightSourceComponent_OpenTK>().Draw(_lightSourceShader, camera);
            }
            //--------------------------------------

            GL.Finish();
            SwapBuffers();
        }

        public void ClearMemory()   //since the libs i use are bindings i assume that i still need to free up memory
        {
            _entityShader.Delete();
            GL.DeleteTextures(1, ref Texture);
            foreach (Entity entity in _renderQueue)
            {
                entity.GetComponent<MeshComponent_OpenTK>().vao.Delete();
                entity.GetComponent<MeshComponent_OpenTK>().vbo.Delete();
                entity.GetComponent<MeshComponent_OpenTK>().ebo.Delete();
            }
            foreach (Entity entity in _lightSourcesRenderQueue)
            {
                entity.GetComponent<LightSourceComponent_OpenTK>().vao.Delete();
                entity.GetComponent<LightSourceComponent_OpenTK>().vbo.Delete();
                entity.GetComponent<LightSourceComponent_OpenTK>().ebo.Delete();
            }
        }
    }
}