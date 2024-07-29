﻿using ArctisAurora.EngineWork.Renderer.Helpers;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace ArctisAurora.EngineWork.Renderer
{
    internal class AVulkanCamera
    {
        //camera buffer
        internal Buffer[] _cameraBuffer;
        internal DeviceMemory[] _camBmemory;
        //keyboard
        internal Dictionary<Silk.NET.GLFW.Keys, bool> _keyStates = new Dictionary<Silk.NET.GLFW.Keys, bool>();
        //variables
        internal Vector3D<float> _pos = new Vector3D<float>(-10, 2, 0);
        internal Vector3D<float> _rotation = new Vector3D<float>(0, 0, 0);
        internal Vector3D<float> _localUp = new Vector3D<float>(0, 1, 0);
        internal Vector3D<float> _front = new Vector3D<float>(0, 0, 1);
        internal Vector3D<float> _localRight = new Vector3D<float>(0, 0, 0);
        //matrices
        internal Matrix4X4<float> _view = Matrix4X4<float>.Identity;
        internal Matrix4X4<float> _projection = Matrix4X4<float>.Identity;
        //controls
        float _speed = 0.5f;
        float _sensitivity = 0.25f;

        internal AVulkanCamera()
        {
            foreach (Silk.NET.GLFW.Keys key in Enum.GetValues(typeof(Silk.NET.GLFW.Keys)))
            {
                _keyStates[key] = false;
            }
            AVulkanBufferHandler.CreateUniformBuffer(ref _cameraBuffer, ref _camBmemory);
        }

        internal void UpdateCameraMatrix(Extent2D _extent, uint currentImage)
        {
            _front.X = MathF.Cos(Scalar.DegreesToRadians(_rotation.X)) * MathF.Cos(Scalar.DegreesToRadians(_rotation.Y));
            _front.Y = MathF.Sin(Scalar.DegreesToRadians(_rotation.Y));
            _front.Z = MathF.Sin(Scalar.DegreesToRadians(_rotation.X)) * MathF.Cos(Scalar.DegreesToRadians(_rotation.Y));
            _front = Vector3D.Normalize(_front);

            _localRight = Vector3D.Normalize(Vector3D.Cross(_front, Vector3D<float>.UnitY));
            _localUp = Vector3D.Normalize(Vector3D.Cross(_localRight, _front));

            _view = Matrix4X4.CreateLookAt(_pos, _pos + _front, Vector3D<float>.UnitY);
            _projection = Matrix4X4.CreatePerspectiveFieldOfView(Scalar.DegreesToRadians(45.0f), _extent.Width / _extent.Height, 0.1f, 5000f);
            _projection.M22 *= -1;

            AVulkanBufferHandler.UpdateUniformBuffer(this, currentImage, ref _camBmemory);
        }

        internal void ProcessMouseMovements(Vector2D<float> _delta, bool _constrainPitch = true)
        {
            _delta *= _sensitivity;

            _rotation.X += _delta.X;
            _rotation.Y -= _delta.Y;

            if (_constrainPitch)
            {
                _rotation.Y = Clamp(_rotation.Y, -89.0f, 89.0f);
            }
        }

        internal void ProcessKeyboard()
        {
            //WASD just wasd man
            if (_keyStates[Silk.NET.GLFW.Keys.W])
            {
                _pos += _speed * _front;
            }
            if (_keyStates[Silk.NET.GLFW.Keys.A])
            {
                _pos += _speed * -_localRight;
            }
            if (_keyStates[Silk.NET.GLFW.Keys.D])
            {
                _pos += _speed * _localRight;
            }
            if (_keyStates[Silk.NET.GLFW.Keys.S])
            {
                _pos += _speed * -_front;
            }
            //EQ up down on unitY
            if (_keyStates[Silk.NET.GLFW.Keys.E])
            {
                _pos += _speed * Vector3D<float>.UnitY;
            }
            if (_keyStates[Silk.NET.GLFW.Keys.Q])
            {
                _pos += _speed * -Vector3D<float>.UnitY;
            }
            //space ctrl local up down
            if (_keyStates[Silk.NET.GLFW.Keys.ControlLeft])
            {
                _pos += _speed * -_localUp;
            }
            if (_keyStates[Silk.NET.GLFW.Keys.Space])
            {
                _pos += _speed * _localUp;
            }
        }

        private float Clamp(float toClamp, float bottom, float top)
        {
            if (toClamp < bottom)
                return bottom;
            else if (toClamp > top)
                return top;
            else
                return toClamp;
        }
    }
}