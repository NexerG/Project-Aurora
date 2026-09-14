using ArctisAurora.Core.UISystem.Controls;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;
using Silk.NET.GLFW;
using System.Numerics;
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

        private void Hover(Vector2 pos)
        {
            RenderWindow.Of(this)?.os.ChangeCursor(GetCursor(pos));
        }

        private void OnExit()
        {
            RenderWindow.Of(this)?.os.ChangeCursor(CursorShape.Arrow);
        }

        private void OnRelease()
        {
            isResizing = false;
        }

        private void Drag(Vector2 lastPos, Vector2 delta)
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

        internal void Resize(Vector2 delta, bool left, bool right, bool top, bool bot)
        {
            Vector3 newControlPos = transform.position;
            Vector3 newControlScale = transform.scale;

            if (left)
            {
                newControlPos += new Vector3(0, 0, delta.X / 2);
                newControlScale += new Vector3(0, 0, -delta.X);
            }
            if(right)
            {
                newControlPos += new Vector3(0, 0, delta.X / 2);
                newControlScale += new Vector3(0, 0, delta.X);
            }

            if (top)
            {
                newControlPos += new Vector3(0, delta.Y / 2, 0);
                newControlScale += new Vector3(0, -delta.Y, 0);
            }
            if (bot)
            {
                newControlPos += new Vector3(0, delta.Y / 2, 0);
                newControlScale += new Vector3(0, delta.Y, 0);
            }
            ref var t = ref transform;
            t.position = newControlPos;
            t.scale = newControlScale;
            CommitTransform();
        }

        internal CursorShape GetCursor(Vector2 pos)
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
