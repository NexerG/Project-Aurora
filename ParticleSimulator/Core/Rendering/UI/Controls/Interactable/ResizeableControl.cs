using Silk.NET.GLFW;
using Silk.NET.Maths;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArctisAurora.EngineWork.Rendering.UI.Controls.Interactable
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
                bool isByEdgeHorizontally = MathF.Abs(MathF.Abs(lastPos.X - transform.position.Z) - MathF.Abs(transform.scale.Z) / 2) < 7;
                bool isByEdgeVertically = MathF.Abs(MathF.Abs(lastPos.Y - transform.position.Y) - MathF.Abs(transform.scale.Y) / 2) < 7;
                if (!(isByEdgeHorizontally || isByEdgeVertically))
                    return;
                isResizing = true;

                if (isByEdgeHorizontally)
                {
                    left = (lastPos.X - transform.position.Z) * MathF.Sign(transform.scale.Z) < 0;
                    right = !left;
                }
                if (isByEdgeVertically)
                {
                    top = (lastPos.Y - transform.position.Y) * MathF.Sign(transform.scale.Y) < 0;
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
            Vector3D<float> newControlPos = transform.position;
            Vector3D<float> newControlScale = transform.scale;

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
            transform.SetWorldPosition(newControlPos);
            transform.SetWorldScale(newControlScale);
        }

        internal CursorShape GetCursor(Vector2D<float> pos)
        {
            bool isByEdgeHorizontally = MathF.Abs(MathF.Abs(pos.X - transform.position.Z) - MathF.Abs(transform.scale.Z) / 2) < 7;
            bool isByEdgeVertically = MathF.Abs(MathF.Abs(pos.Y - transform.position.Y) - MathF.Abs(transform.scale.Y) / 2) < 7;

            if (!(isByEdgeHorizontally || isByEdgeVertically))
                return CursorShape.Arrow;

            bool left = (pos.X - transform.position.Z) < 0;
            if (!isByEdgeVertically)
            {
                return CursorShape.HResize;
            }

            bool up = (pos.Y - transform.position.Y) < 0;
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
