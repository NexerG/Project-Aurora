using ArctisAurora.EngineWork.Rendering.Helpers;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using System.Runtime.CompilerServices;
using Buffer = Silk.NET.Vulkan.Buffer;
using Keys = Silk.NET.GLFW.Keys;

namespace ArctisAurora.EngineWork.Rendering
{   
    internal class AuroraCamera
    {
        //camera buffer
        internal Buffer[] _cameraBuffer;
        internal DeviceMemory[] _camBmemory;
        //keyboard
        internal Dictionary<Keys, bool> _keyStates = new Dictionary<Keys, bool>();
        //variables
        internal Vector3D<float> _pos = new Vector3D<float>(0, 0, 0);
        internal Vector3D<float> _rotation = new Vector3D<float>(0, 0, 0);
        internal Vector3D<float> _localUp = new Vector3D<float>(0, 1, 0);
        internal Vector3D<float> _front = new Vector3D<float>(0, 0, -1);
        internal Vector3D<float> _localRight = new Vector3D<float>(0, 0, 0);
        //matrices
        internal Matrix4X4<float> _view = Matrix4X4<float>.Identity;
        internal Matrix4X4<float> _projection = Matrix4X4<float>.Identity;
        //controls
        float _speed = 0.5f;
        float _sensitivity = 0.25f;
        //control vars
        private bool _firstMove = true;
        private double _lastX, _lastY;

        internal AuroraCamera()
        {
            foreach (Keys key in Enum.GetValues(typeof(Keys)))
            {
                _keyStates[key] = false;
            }

            ulong bufferSize = (ulong)Unsafe.SizeOf<UBO>();
            _cameraBuffer = new Buffer[Renderer.swapchainImageCount];
            _camBmemory = new DeviceMemory[Renderer.swapchainImageCount];
            for (int i = 0; i < Renderer.swapchainImageCount; i++)
            {
                AVulkanBufferHandler.CreateBuffer(bufferSize, ref _cameraBuffer[i], ref _camBmemory[i], BufferUsageFlags.UniformBufferBit | BufferUsageFlags.TransferDstBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
            }
        }

        internal void UpdateCameraMatrix(Extent2D _extent, uint currentImage, uint cameraIndex)
        {

            switch (Renderer.renderingModules[cameraIndex].rendererType)
            {
                case ERendererTypes.Rasterizer:
                    _front.X = MathF.Cos(Scalar.DegreesToRadians(_rotation.X)) * MathF.Cos(Scalar.DegreesToRadians(_rotation.Y));
                    _front.Y = MathF.Sin(Scalar.DegreesToRadians(_rotation.Y));
                    _front.Z = MathF.Sin(Scalar.DegreesToRadians(_rotation.X)) * MathF.Cos(Scalar.DegreesToRadians(_rotation.Y));
                    _front = Vector3D.Normalize(_front);

                    _localRight = Vector3D.Normalize(Vector3D.Cross(_front, Vector3D<float>.UnitY));
                    _localUp = Vector3D.Normalize(Vector3D.Cross(_localRight, _front));

                    _view = Matrix4X4.CreateLookAt(_pos, _pos + _front, Vector3D<float>.UnitY);
                    _projection = Matrix4X4.CreatePerspectiveFieldOfView(Scalar.DegreesToRadians(60.0f), _extent.Width / _extent.Height, 0.1f, 512f);
                    _projection.M22 *= -1;
                    break;

                case ERendererTypes.Pathtracer:
                    Matrix4X4<float> _tempView;
                    Matrix4X4<float> _tempProjection;

                    Matrix4X4.Invert(_view, out _tempView);
                    Matrix4X4.Invert(_projection, out _tempProjection);
                    _view = _tempView;
                    _projection = _tempProjection;
                    break;

                case ERendererTypes.UITemp:
                    _view = Matrix4X4.CreateLookAt(Vector3D<float>.Zero, _front, _localUp);
                    _projection = Matrix4X4.CreateOrthographicOffCenter(0, _extent.Width, 0, _extent.Height, 0.01f, 512f);
                    break;
                default:
                    break;
            }

            UBO _ubo = new UBO()
            {
                _view = _view,
                _projection = _projection,
                //_lightProjection = Rasterizer._lightsToRender[0].GetComponent<LightsourceComponent>()._lightProjection,
                //_lightView = Rasterizer._lightsToRender[0].GetComponent<LightsourceComponent>()._lightView,
                //_camPos = _camera._pos
            };

            AVulkanBufferHandler.UpdateBuffer(ref _ubo, ref _cameraBuffer[currentImage], ref _camBmemory[currentImage], BufferUsageFlags.None);
        }

        internal void ProcessMouseMovements(double xPos, double yPos, bool _constrainPitch = true)
        {
            if (Renderer.renderingModules[0].rendererType == ERendererTypes.UITemp)
            {
                return;
            }
            if (_firstMove)
            {
                _lastX = xPos;
                _lastY = yPos;
                _firstMove = false;
            }

            Vector2D<float> _delta = new Vector2D<float>((float)(xPos - _lastX), (float)(yPos - _lastY));
            _lastX = xPos;
            _lastY = yPos;

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
            if (_keyStates[Keys.W])
            {
                _pos += _speed * _front;
            }
            if (_keyStates[Keys.A])
            {
                _pos += _speed * -_localRight;
            }
            if (_keyStates[Keys.D])
            {
                _pos += _speed * _localRight;
            }
            if (_keyStates[Keys.S])
            {
                _pos += _speed * -_front;
            }
            //EQ up down on unitY
            if (_keyStates[Keys.E])
            {
                _pos += _speed * Vector3D<float>.UnitY;
            }
            if (_keyStates[Keys.Q])
            {
                _pos += _speed * -Vector3D<float>.UnitY;
            }
            //space ctrl local up down
            if (_keyStates[Keys.ControlLeft])
            {
                _pos += _speed * -_localUp;
            }
            if (_keyStates[Keys.Space])
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

        internal static Vector2D<float> GetPixelSizeInWorldSpace(float left, float right, float bot, float top, int screenWidth, int screenHeight)
        {
            float worldWidth = right - left;
            float worldHeight = top - bot;

            float pixelWidth = worldWidth / screenWidth;
            float pixelHeight = worldHeight / screenHeight;

            return new Vector2D<float>(pixelWidth, pixelHeight);
        }

        internal void UpdateCameraMatrix(Extent2D windowExtent, uint imageIndex, int i)
        {
            throw new NotImplementedException();
        }
    }
}