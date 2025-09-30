using ArctisAurora.EngineWork.EngineEntity;
using ArctisAurora.EngineWork.Rendering.UI.Controls;
using ArctisAurora.EngineWork.Rendering.UI.Controls.Containers;
using ArctisAurora.EngineWork.Rendering.UI.Controls.Interactable;
using Silk.NET.GLFW;
using Silk.NET.Maths;

namespace ArctisAurora.EngineWork.Physics.UICollision
{
    internal unsafe class UICollisionHandling
    {
        internal static UICollisionHandling instance;
        internal bool isInWindow = true;
        internal AbstractInteractableControl dragging;
        internal AbstractContainerControl container;
        internal ContextMenuControl defaultContextMenu;

        internal Vector2D<float> lastMousePos;
        internal Vector2D<float> delta;

        internal UICollisionHandling()
        {
            instance = this;
        }

        public void SolveHover(Vector2D<float> mousePos)
        {
            Vector2D<float>[] localVerts = new Vector2D<float>[4];
            AbstractInteractableControl topMost = null;
            float topZ = float.MaxValue;
            foreach (AbstractInteractableControl entity in EntityManager.interactableControls)
            {
                // check for hovering on each entity
                bool isHovering = SolvePositions(entity, mousePos, localVerts);
                if (isHovering)
                {
                    if(entity.transform.position.X < topZ)
                    {
                        topZ = entity.transform.position.X;
                        topMost = entity;
                    }
                }
                else
                {
                    entity.ResolveExit();
                }
            }

            if(topMost != null && dragging == null)
            {
                topMost.ResolveEnter();
                topMost.ResolveHover(mousePos);
            }
        }

        public void SolveLMB(Vector2D<float> mousePos)
        {
            Vector2D<float>[] localVerts = new Vector2D<float>[4];
            bool pressed = InputHandler.instance.IsKeyDown(new Keybind(MouseButton.Left));

            AbstractInteractableControl topMost = null;
            float topZ = float.MaxValue;
            foreach (AbstractInteractableControl entity in EntityManager.interactableControls)
            {
                // check for hovering on each entity
                bool isHovering = SolvePositions(entity, mousePos, localVerts);
                if (isHovering && pressed)
                {
                    if (entity.transform.position.X < topZ)
                    {
                        topZ = entity.transform.position.X;
                        topMost = entity;
                    }
                }
                else if(!pressed)
                {
                    entity.ResolveRelease();
                }
            }
            if (topMost != null)
            {
                topMost.ResolveClick(lastMousePos, delta);
            }
        }

        public void SolveRMB(Vector2D<float> mousePos)
        {
            Vector2D<float>[] localVerts = new Vector2D<float>[4];
            bool pressed = InputHandler.instance.IsKeyDown(new Keybind(MouseButton.Right));
            bool found = false;

            AbstractInteractableControl topMost = null;
            float topZ = float.MaxValue;
            foreach (AbstractInteractableControl entity in EntityManager.interactableControls)
            {
                // check for hovering on each entity
                bool isHovering = SolvePositions(entity, mousePos, localVerts);
                if (isHovering && pressed)
                {
                    if (entity.transform.position.X < topZ)
                    {
                        topZ = entity.transform.position.X;
                        topMost = entity;
                    }
                }
                else if (!pressed)
                {
                    entity.ResolveAltRelease();
                    found = true;
                }
            }
            if (topMost != null)
            {
                topMost.ResolveAltClick();
            }
            if (!found && defaultContextMenu != null)
            {
                defaultContextMenu.Open();
            }
        }


        public void SolveDrag(Vector2D<float> mousePos)
        {
            if (dragging != null)
            {
                dragging.ResolveDrag(lastMousePos, delta);
            }
        }

        public void IsInWindow(WindowHandle* handle, bool isInWindow)
        {
            this.isInWindow = isInWindow;
        }

        private bool SolvePositions(VulkanControl entity, Vector2D<float> pos, Vector2D<float>[] localVerts)
        {
            float offsetX = entity.controlData.quadData.offsets.offset1.Z;
            float offsetY = entity.controlData.quadData.offsets.offset1.Y;
            // check for hovering on each entity
            localVerts[0] = new Vector2D<float>(-0.5f + offsetX, -0.5f + offsetY);
            localVerts[1] = new Vector2D<float>(0.5f + offsetX, -0.5f + offsetY);
            localVerts[2] = new Vector2D<float>(0.5f + offsetX, 0.5f + offsetY);
            localVerts[3] = new Vector2D<float>(-0.5f + offsetX, 0.5f + offsetY);

            localVerts = TransformToWorld(entity.transform, localVerts);
            return IsPointInQuad(pos, localVerts);
        }

        private Vector2D<float>[] TransformToWorld(Transform transform, Vector2D<float>[] localVerts)
        {
            Vector2D<float>[] worldVerts = new Vector2D<float>[4];

            //float cos = MathF.Cos(transform.rotation);
            //float sin = MathF.Sin(transform.rotation);

            for (int i = 0; i < 4; i++)
            {
                Vector2D<float> scaled = new Vector2D<float>(localVerts[i].X * transform.scale.Z, localVerts[i].Y * transform.scale.Y);
                worldVerts[i] = new Vector2D<float>(scaled.X + transform.position.Z, scaled.Y + transform.position.Y);
            }

            return worldVerts;
        }

        private bool IsPointInQuad(Vector2D<float> point, Vector2D<float>[] quadVerts)
        {
            bool sameSide = true;

            for (int i = 0; i < 4; i++)
            {
                Vector2D<float> a = quadVerts[i];
                Vector2D<float> b = quadVerts[(i + 1) % 4];
                Vector2D<float> edge = b - a;
                Vector2D<float> toPoint = point - a;

                float cross = edge.X * toPoint.Y - edge.Y * toPoint.X;

                if (i == 0)
                    sameSide = cross > 0;
                else if ((cross > 0) != sameSide)
                    return false;
            }

            return true;
        }
    }
}
