using ArctisAurora.Core.UISystem.Controls;
using ArctisAurora.EngineWork.Rendering;
using Silk.NET.GLFW;
using Silk.NET.Maths;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArctisAurora.Core.UISystem.Controls.Interactable
{
    public class ResizeableControl : PanelControl
    {
        bool isResizing = false;
        bool left = false;
        bool right = false;
        bool top = false;
        bool bot = false;

        public ResizeableControl()
        {
            RegisterHover(Hover);
            RegisterOnExit(OnExit);
            RegisterOnDrag(Drag);
            RegisterOnRelease(OnRelease);
        }

        private void Hover(Vector2D<float> pos)
        {
            AGlfwWindow.ChangeCursor(GetCursor(pos));
        }

        private void OnExit()
        {
            AGlfwWindow.ChangeCursor(CursorShape.Arrow);
        }

        private void OnRelease()
        {
            isResizing = false;
        }

        private void Drag(Vector2D<float> lastPos, Vector2D<float> delta)
        {
            if (!isResizing)
            {
                bool isByEdgeHorizontally = MathF.Abs(MathF.Abs(lastPos.X - PoolTransform.position.Z) - MathF.Abs(PoolTransform.scale.Z) / 2) < 7;
                bool isByEdgeVertically = MathF.Abs(MathF.Abs(lastPos.Y - PoolTransform.position.Y) - MathF.Abs(PoolTransform.scale.Y) / 2) < 7;
                if (!(isByEdgeHorizontally || isByEdgeVertically))
                    return;
                isResizing = true;

                if (isByEdgeHorizontally)
                {
                    left = (lastPos.X - PoolTransform.position.Z) * MathF.Sign(PoolTransform.scale.Z) < 0;
                    right = !left;
                }
                if (isByEdgeVertically)
                {
                    top = (lastPos.Y - PoolTransform.position.Y) * MathF.Sign(PoolTransform.scale.Y) < 0;
                    bot = !top;
                }
            }
            if (isResizing)
            {
                Resize(delta, left, right, top, bot);
            }
        }

        internal void Resize(Vector2D<float> delta, bool left, bool right, bool top, bool bot)
        {
            Vector3D<float> newControlPos = PoolTransform.position;
            Vector3D<float> newControlScale = PoolTransform.scale;

            if (left)
            {
                newControlPos += new Vector3D<float>(0, 0, delta.X / 2);
                newControlScale += new Vector3D<float>(0, 0, -delta.X);
            }
            if(right)
            {
                newControlPos += new Vector3D<float>(0, 0, delta.X / 2);
                newControlScale += new Vector3D<float>(0, 0, delta.X);
            }

            if (top)
            {
                newControlPos += new Vector3D<float>(0, delta.Y / 2, 0);
                newControlScale += new Vector3D<float>(0, -delta.Y, 0);
            }
            if (bot)
            {
                newControlPos += new Vector3D<float>(0, delta.Y / 2, 0);
                newControlScale += new Vector3D<float>(0, delta.Y, 0);
            }
            ref var t = ref PoolTransform;
            t.position = newControlPos;
            t.scale = newControlScale;
            ControlPool.MarkContentDirty();
        }

        internal CursorShape GetCursor(Vector2D<float> pos)
        {
            bool isByEdgeHorizontally = MathF.Abs(MathF.Abs(pos.X - PoolTransform.position.Z) - MathF.Abs(PoolTransform.scale.Z) / 2) < 7;
            bool isByEdgeVertically = MathF.Abs(MathF.Abs(pos.Y - PoolTransform.position.Y) - MathF.Abs(PoolTransform.scale.Y) / 2) < 7;

            if (!(isByEdgeHorizontally || isByEdgeVertically))
                return CursorShape.Arrow;

            bool left = (pos.X - PoolTransform.position.Z) < 0;
            if (!isByEdgeVertically)
            {
                return CursorShape.HResize;
            }

            bool up = (pos.Y - PoolTransform.position.Y) < 0;
            if (!isByEdgeHorizontally)
            {
                return CursorShape.VResize;
            }

            switch (left, up)
            {
                case (true, true):
                    return CursorShape.NwseResize;
                case (true, false):
                    return CursorShape.NeswResize;
                case (false, true):
                    return CursorShape.NeswResize;
                case (false, false):
                    return CursorShape.NwseResize;
            }
        }
    }
}
