using Microsoft.VisualBasic.Devices;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Keys = OpenTK.Windowing.GraphicsLibraryFramework.Keys;

namespace ArctisAurora.EngineWork.Rendering
{
    public class Camera
    {
        //vars init
        public Vector3 pos = new Vector3(-500f, -500f, 350f);

        //directions
        Vector3 orientation = new Vector3(0,0,0);
        Vector3 up = new Vector3(0,0,0);
        Matrix4 pv;
        Vector3 front;
        Vector3 right;

        //controls
        float speed = 0.01f;
        float sensitivity = .25f;

        public Camera()
        {

        }

        public void Matrix(ShaderClass shader, string uniform)
        {
            GL.UniformMatrix4(GL.GetUniformLocation(shader.program, uniform), false, ref pv);
        }

        public void updateMatrix()
        {
            front.X = MathF.Cos(MathHelper.DegreesToRadians(orientation.X)) * MathF.Cos(MathHelper.DegreesToRadians(orientation.Y));
            front.Y = MathF.Sin(MathHelper.DegreesToRadians(orientation.Y));
            front.Z = MathF.Sin(MathHelper.DegreesToRadians(orientation.X)) * MathF.Cos(MathHelper.DegreesToRadians(orientation.Y));
            front = Vector3.Normalize(front);

            right = Vector3.Normalize(Vector3.Cross(front, Vector3.UnitY));
            up = Vector3.Normalize(Vector3.Cross(right, front));

            Matrix4 view = Matrix4.LookAt(pos, pos + front, up);
            Matrix4 projection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(60), 1920 / 1080, 0.1f, 5000f);
            pv = view * projection;
        }

        internal void ProcessMouseMovement(Vector2 delta, bool constrainPitch = true)
        {
            delta *= sensitivity;

            orientation.X += delta.X;
            orientation.Y -= delta.Y;

            if (constrainPitch)
            {
                orientation.Y = MathHelper.Clamp(orientation.Y, -89.0f,89.0f);
            }
        }

        internal void ProcessKeyboard(KeyboardState keyboard)
        {
            if (keyboard.IsKeyDown(Keys.W))
            {
                pos += speed * front;
            }
            if (keyboard.IsKeyDown(Keys.A))
            {
                pos += speed * -right;
            }
            if (keyboard.IsKeyDown(Keys.D))
            {
                pos += speed * right;
            }
            if (keyboard.IsKeyDown(Keys.S))
            {
                pos += speed * -front;
            }
            if (keyboard.IsKeyDown(Keys.Space))
            {
                pos += speed * up;
            }
            if (keyboard.IsKeyDown(Keys.LeftControl))
            {
                pos += speed * -up;
            }
            if (keyboard.IsKeyDown(Keys.E))
            {
                pos += speed * Vector3.UnitY;
            }
            if (keyboard.IsKeyDown(Keys.Q))
            {
                pos += speed * -Vector3.UnitY;
            }
        }
    }
}