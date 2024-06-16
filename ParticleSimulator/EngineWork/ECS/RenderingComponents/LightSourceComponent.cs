using ArctisAurora.EngineWork.ComponentBehaviour;
using ArctisAurora.EngineWork.Model;
using ArctisAurora.EngineWork.Rendering.Renderers.OpenTK;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace ArctisAurora.EngineWork.ECS.RenderingComponents
{
    internal class LightSourceComponent : EntityComponent
    {
        //the model
        internal Mesh _model;
        internal Vector4 _lightColor = new Vector4(1f, 1f, 1f, 1f);
        internal float _lightIntensity = 1f;
        internal float _attenuationRadius = 5000f;

        //A & E buffers
        internal VAO vao;
        internal VBO vbo = new VBO();
        internal EBO ebo = new EBO();

        //instancing
        internal Matrix4 instanceMatrix = Matrix4.Identity;

        public LightSourceComponent()
        {
            _model = new Mesh();
            if(_model!=null)
            {
                vao = new VAO();
                vao.Bind();

                ebo.BufferElementData(_model.indices);
                vbo.BufferVertexData(_model.vertices);

                //tell the gpu what vertex buffer part is what (vertice, UV, normal)
                vao.LinkAttrib(vbo, 0, 3, VertexAttribPointerType.Float, 8 * sizeof(float), 0);
                vao.LinkAttrib(vbo, 1, 2, VertexAttribPointerType.Float, 8 * sizeof(float), 3 * sizeof(float));
                vao.LinkAttrib(vbo, 2, 3, VertexAttribPointerType.Float, 8 * sizeof(float), 5 * sizeof(float));
                vao.Unbind();
            }
        }

        public override void OnStart()
        {
            Rasterization._rendererInstance.LightToRenderQueue(parent);
        }

        internal void AddMesh(Mesh m)
        {
            _model = m;
        }

        internal void SingletonMatrix()
        {
            Vector3 pos = new Vector3(parent.transform.position.X, parent.transform.position.Y, parent.transform.position.Z);
            OpenTK.Mathematics.Quaternion q = OpenTK.Mathematics.Quaternion.FromEulerAngles(parent.transform.rotation);

            Matrix4 transformation = Matrix4.Identity;
            transformation *= Matrix4.CreateScale(parent.transform.scale);
            transformation *= Matrix4.CreateFromQuaternion(q);
            transformation *= Matrix4.CreateTranslation(pos);
            instanceMatrix = transformation;
        }

        public virtual void Draw(ShaderClass shader, Camera camera)
        {
            vao.Bind();

            SingletonMatrix();
            GL.Uniform4(GL.GetUniformLocation(shader.program, "lightColor"), _lightColor.X, _lightColor.Y, _lightColor.Z, _lightColor.W);
            GL.UniformMatrix4(GL.GetUniformLocation(shader.program, "model"), false, ref instanceMatrix);
            GL.DrawElements(PrimitiveType.Triangles, _model.indices.Length * sizeof(uint) / sizeof(int), DrawElementsType.UnsignedInt, 0);
            
            vao.Unbind();
        }

    }
}
