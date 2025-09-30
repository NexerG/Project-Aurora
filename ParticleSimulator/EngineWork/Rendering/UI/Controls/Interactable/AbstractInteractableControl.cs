using ArctisAurora.EngineWork.Physics.UICollision;
using ArctisAurora.EngineWork.Rendering.Modules;
using Silk.NET.Maths;
using System.Xml.Serialization;

namespace ArctisAurora.EngineWork.Rendering.UI.Controls.Interactable
{
    public abstract class AbstractInteractableControl : PanelControl
    {
        // EVENTS
        public event Action<Vector2D<float>> hover;
        public event Action onEnter;
        public event Action onExit;

        public event Action onClick;
        public event Action onAltClick;

        public event Action onDoubleClick;

        public event Action onRelease;
        public event Action onAltRelease;

        public event Action<Vector2D<float>, Vector2D<float>> onDrag;
        public event Action onDragStop;

        private bool entered = false;
        private bool clicked = false;
        private bool altClicked = false;
        private bool dragging = false;

        private DateTime lastClick = DateTime.Now;

        // EXTRAS
        internal ContextMenuControl contextMenu;

        internal AbstractInteractableControl()
        {
            EntityManager.AddInteractableControl(this);
        }

        // HOVER
        internal void RegisterHover(Action<Vector2D<float>> action)
        {
            hover += action;
        }

        internal void ResolveHover(Vector2D<float> pos)
        {
            if (clicked)
            {
                dragging = true;
                UICollisionHandling.instance.dragging = this;
                return;
            }
            hover?.Invoke(pos);
        }

        // ENTER
        internal void RegisterOnEnter(Action action)
        {
            onEnter += action;
        }

        internal virtual void ResolveEnter()
        {
            if (!entered)
            {
                onEnter?.Invoke();
            }
            entered = true;
        }
        
        // EXIT
        internal void RegisterOnExit(Action action)
        {
            onExit += action;
        }

        internal virtual void ResolveExit()
        {
            if (entered)
            {
                onExit?.Invoke();
            }
            entered = false;
        }

        // DRAG
        internal void RegisterOnDrag(Action<Vector2D<float>, Vector2D<float>> action)
        {
            onDrag += action;
        }

        internal virtual void ResolveDrag(Vector2D<float> lastPos,Vector2D<float> delta)
        {
            //if (onDrag != null)
            //{
            onDrag?.Invoke(lastPos, delta);
            //}
        }

        internal virtual void RegisterDragStop(Action action)
        {
            onDragStop += action;
        }

        internal virtual void StopDrag()
        {
            onDragStop?.Invoke();
            UICollisionHandling.instance.dragging = null;
        }

        // CLICK
        internal void RegisterOnClick(Action action)
        {
            onClick += action;
        }

        internal virtual void ResolveClick(Vector2D<float> oldPos, Vector2D<float> delta)
        {
            if (!clicked)
            {
                DateTime click = DateTime.Now;
                TimeSpan span = click - lastClick;
                lastClick = click;
                if (span.TotalMilliseconds < Engine.doubleClickTime)
                {
                    ResolveDoubleClick();
                    clicked = true;
                    return;
                }

                onClick?.Invoke();
            }
            /*else
            {
                TimeSpan t = DateTime.Now - lastClick;
                if (t.TotalMilliseconds < Engine.doubleClickTime)
                    return;
                ResolveDrag(oldPos, delta);
            }*/
            clicked = true;
        }

        // DOUBLE CLICK
        internal void RegisterDoubleClick(Action action)
        {
            onDoubleClick += action;
        }

        internal virtual void ResolveDoubleClick()
        {
            onDoubleClick?.Invoke();
        }

        // RELEASE
        internal void RegisterOnRelease(Action action)
        {
            onRelease += action;
        }

        internal virtual void ResolveRelease()
        {
            if (dragging)
            {
                StopDrag();
            }
            if (clicked)
            {
                onRelease?.Invoke();
            }
            clicked = false;
        }

        // ALT CLICK
        internal void RegisterAltClick(Action action)
        {
            onAltClick += action;
        }

        internal virtual void ResolveAltClick()
        {
            if (!altClicked)
            {
                onAltClick?.Invoke();
            }
            altClicked = true;
        }

        // ALT RELEASE
        internal void RegisterAltRelease(Action action)
        {
            onAltRelease += action;
        }

        internal virtual void ResolveAltRelease()
        {
            if (altClicked)
            {
                onAltRelease?.Invoke();
            }
            altClicked = false;
        }
    }
}
