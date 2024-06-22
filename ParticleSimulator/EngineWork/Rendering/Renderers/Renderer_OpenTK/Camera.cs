using Microsoft.VisualBasic.Devices;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Keys = OpenTK.Windowing.GraphicsLibraryFramework.Keys;

namespace ArctisAurora.EngineWork.Rendering.Renderers.OpenTK
{
    public class Camera
    {
        //vars init
        public Vector3 pos = new Vector3(0.0f, 0.0f, -5.0f);

        //directions
        Vector3 rotation = new Vector3(0, 0, 0);
        Vector3 localUp = new Vector3(0, 1, 0);
        Vector3 front = new Vector3(0, 0, 1);
        Vector3 localRight;

        internal Matrix4 view;
        internal Matrix4 projection;
        internal Matrix4 pv;

        //controls
        float speed = 0.001f;
        float sensitivity = .25f;

        public Camera()
        {

        }

        public void updateMatrix()
        {
            front.X = MathF.Cos(MathHelper.DegreesToRadians(rotation.X)) * MathF.Cos(MathHelper.DegreesToRadians(rotation.Y));
            front.Y = MathF.Sin(MathHelper.DegreesToRadians(rotation.Y));
            front.Z = MathF.Sin(MathHelper.DegreesToRadians(rotation.X)) * MathF.Cos(MathHelper.DegreesToRadians(rotation.Y));
            front = Vector3.Normalize(front);

            localRight = Vector3.Normalize(Vector3.Cross(front, Vector3.UnitY));
            localUp = Vector3.Normalize(Vector3.Cross(localRight, front));

            view = Matrix4.LookAt(pos, pos + front, Vector3.UnitY);
            projection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(60), 1920 / 1080, 0.1f, 5000f);
            pv = view * projection;
        }

        internal void ProcessMouseMovement(Vector2 delta, bool constrainPitch = true)
        {
            delta *= sensitivity;

            rotation.X += delta.X;
            rotation.Y -= delta.Y;

            if (constrainPitch)
            {
                rotation.Y = MathHelper.Clamp(rotation.Y, -89.0f, 89.0f);
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
                pos += speed * -localRight;
            }
            if (keyboard.IsKeyDown(Keys.D))
            {
                pos += speed * localRight;
            }
            if (keyboard.IsKeyDown(Keys.S))
            {
                pos += speed * -front;
            }
            if (keyboard.IsKeyDown(Keys.Space))
            {
                pos += speed * localUp;
            }
            if (keyboard.IsKeyDown(Keys.LeftControl))
            {
                pos += speed * -localUp;
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