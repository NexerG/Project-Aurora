using ArctisAurora.EngineWork.Physics.UICollision;
using ArctisAurora.EngineWork.Rendering.Helpers;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using System.Diagnostics;

namespace ArctisAurora.EngineWork.Rendering.UI.Controls.Containers
{
    [A_VulkanEnum("DockMode")]
    public enum DockMode
    {
        fill, left, right, top, bottom, unknown
    }

    [A_VulkanContainer("Dock")]
    public class DockingControl : AbstractContainerControl
    {
        public VulkanControl top;
        public VulkanControl bottom;
        public VulkanControl left;
        public VulkanControl right;
        public VulkanControl center;

        public Vector2D<float> splitSize;
        public Vector2D<float> spaceLeft;
        public Vector2D<float> location;

        public DockingControl()
        {

        }

        public DockingControl(VulkanControl parent) : base(parent)
        {
            controlData.style.tint = new Vector3D<float>(0.22f, 0.22f, 0.22f);

            RegisterHover(Hovering);
            if (parent != null)
            {

            }
            else
            {
                uint halfWidth = Engine.window.windowSize.Width / 2;
                uint halfHeight = Engine.window.windowSize.Height / 2;
                Vector3D<float> pos = new(1, halfHeight, halfWidth);
                transform.SetWorldPosition(pos);
                transform.SetWorldScale(pos * 2);

                float splitH = Engine.window.windowSize.Height / 3;
                float splitW = Engine.window.windowSize.Width / 3;
                splitSize = new Vector2D<float>(splitW, splitH);
                spaceLeft = new Vector2D<float>(Engine.window.windowSize.Width, Engine.window.windowSize.Height);
                location = new Vector2D<float>(halfWidth, halfHeight);
            }
            UpdateControlData();
        }

        public override void OnStart()
        {
            base.OnStart();
        }

        public override void AddControlToContainer(VulkanControl control)
        {
            //DockMode mode = ResolveDockType(control);
            Dock(control, control.dockMode);
        }

        private void Hovering(Vector2D<float> pos)
        {
            UICollisionHandling.instance.container = this;
        }

        internal DockMode ResolveDockType(VulkanControl control)
        {
            Vector2D<float> pos = new Vector2D<float>(control.transform.position.Z, control.transform.position.Y);
            Vector2D<float> planarPos = new Vector2D<float>(transform.position.Z, transform.position.Y);
            float dist = Vector2D.Distance(planarPos, pos);
            float factor = MathF.Min(transform.scale.Z, transform.scale.Y) / 3;
            if (factor > dist)
            {
                return DockMode.fill;
            }
            float horizontalFactor = MathF.Abs(pos.X - planarPos.X);
            if (horizontalFactor > factor)
            {
                Vector2D<float> e1 = new Vector2D<float>(Engine.window.windowSize.Width / 2, 0);
                Vector2D<float> e2 = new Vector2D<float>(Engine.window.windowSize.Width / 2, Engine.window.windowSize.Height);
                bool isRight = IsRightOfSegement(pos, e1, e2);
                if (isRight)
                {
                    return DockMode.right;
                }
                else
                {
                    return DockMode.left;
                }
            }
            float verticalFactor = MathF.Abs(pos.Y - planarPos.Y);
            if (verticalFactor > factor)
            {
                Vector2D<float> e1 = new Vector2D<float>(0, Engine.window.windowSize.Height / 2);
                Vector2D<float> e2 = new Vector2D<float>(Engine.window.windowSize.Width, Engine.window.windowSize.Height / 2);
                bool isTop = IsRightOfSegement(pos, e1, e2);
                if (isTop)
                {
                    return DockMode.top;
                }
                else
                {
                    return DockMode.bottom;
                }
            }

            return DockMode.unknown;
        }

        internal void Dock(VulkanControl control, DockMode mode)
        {
            uint halfWidth = Engine.window.windowSize.Width / 2;
            uint halfHeight = Engine.window.windowSize.Height / 2;
            if (parent != null && parent.GetType() != typeof(WindowControl))
            {
                halfWidth = (uint)(parent.transform.scale.X / 2);
                halfHeight = (uint)(parent.transform.scale.Y / 2);
            }
            Vector3D<float> pos = new(halfHeight, halfWidth, -10);
            transform.SetWorldPosition(pos);
            transform.SetWorldScale(pos * 2);

            float splitW = Engine.window.windowSize.Width / 3;
            float splitH = Engine.window.windowSize.Height / 3;
            splitSize = new Vector2D<float>(splitW, splitH);
            spaceLeft = new Vector2D<float>(Engine.window.windowSize.Width, Engine.window.windowSize.Height);
            location = new Vector2D<float>(halfWidth, halfHeight);
            Vector2D<float> center = new Vector2D<float>(location.X, location.Y);
            switch (mode)
            {
                case DockMode.left:
                    if (left == null)
                    {
                        left = control;
                        center.X = splitSize.X / 2;
                        control.transform.SetWorldPosition(new Vector3D<float>(center.X, center.Y, transform.position.Z + 0.1f));
                        control.transform.SetWorldScale(new Vector3D<float>(splitSize.X, spaceLeft.Y, 1.0f));
                        spaceLeft.X -= splitSize.X;
                        location.X += splitSize.X / 2;
                    }
                    break;
                case DockMode.right:
                    if (right == null)
                    {
                        right = control;
                        center.X = transform.scale.Z - (splitSize.X / 2);
                        control.transform.SetWorldPosition(new Vector3D<float>(transform.position.X - 0.1f, center.Y, center.X));
                        control.transform.SetWorldScale(new Vector3D<float>(1, spaceLeft.Y, splitSize.X));
                        spaceLeft.X -= splitSize.X;
                        location.X -= splitSize.X / 2;
                    }
                    break;
                case DockMode.top:
                    if (top == null)
                    {
                        top = control;
                        center.Y = splitSize.Y / 2;
                        control.transform.SetWorldPosition(new Vector3D<float>(transform.position.X - 0.1f, center.Y, center.X));
                        control.transform.SetWorldScale(new Vector3D<float>(1, splitSize.Y, spaceLeft.X));
                        spaceLeft.Y -= splitSize.Y;
                        location.Y += splitSize.Y / 2;
                    }
                    break;
                case DockMode.bottom:
                    if (bottom == null)
                    {
                        bottom = control;
                        center.Y = transform.scale.Y - (splitSize.Y / 2);
                        control.transform.SetWorldPosition(new Vector3D<float>(transform.position.X - 0.1f, center.Y, center.X));
                        control.transform.SetWorldScale(new Vector3D<float>(1, splitSize.Y, spaceLeft.X));
                        spaceLeft.Y -= splitSize.Y;
                        location.Y -= splitSize.Y / 2;
                    }
                    break;
                case DockMode.fill:
                    if(top != null)
                    {
                        center.Y -= top.transform.scale.Y / 2;
                    }
                    if(bottom != null)
                    {
                        center.Y += bottom.transform.scale.Y / 2;
                    }
                    if(left != null)
                    {
                        center.X -= left.transform.scale.X / 2;
                    }
                    if(right != null)
                    {
                        center.X += right.transform.scale.X / 2;
                    }
                    control.transform.SetWorldPosition(new Vector3D<float>(transform.position.X - 0.1f, center.Y, center.X));
                    control.transform.SetWorldScale(new Vector3D<float>(1, spaceLeft.Y, spaceLeft.X));
                    break;
                default: break;
            }
        }

        private void Fill()
        {
            Console.WriteLine("Filling");
        }

        private static bool IsRightOfSegement(Vector2D<float> point, Vector2D<float> e1, Vector2D<float> e2)
        {
            var ab = new Vector2D<float>(e2.X - e1.X, e2.Y - e1.Y);
            var ap = new Vector2D<float>(point.X - e1.X, point.Y - e1.Y);

            float cross = ab.X * ap.Y - ab.Y * ap.X;

            return cross < 0; // true = point is to the right of the edge from e1 to e2
        }

    }
}
