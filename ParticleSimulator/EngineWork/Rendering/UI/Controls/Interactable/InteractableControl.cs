using Silk.NET.Maths;
using Silk.NET.Vulkan;

namespace ArctisAurora.EngineWork.Rendering.UI.Controls.Interactable
{
    internal abstract class InteractableControl : PanelControl
    {
        public event Action onEnter;
        public event Action onExit;

        public event Action onClick;
        public event Action onAltClick;

        public event Action onDoubleClick;

        public event Action onRelease;
        public event Action onAltRelease;

        public event Action onDrag;
        public event Action onDragStop;

        private bool entered = false;
        private bool clicked = false;
        private bool altClicked = false;
        private bool dragging = false;

        private DateTime lastClick = DateTime.Now;

        internal InteractableControl()
        {
            EntityManager.AddInteractableControl(this);
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
            if (entered && clicked && !dragging)
            {
                onDrag?.Invoke();
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
        internal void RegisterOnDrag(Action action)
        {
            onDrag += action;
        }

        internal virtual void ResolveDrag()
        {
            onDrag?.Invoke();
        }

        internal virtual void RegisterDragStop(Action action)
        {
            onDragStop += action;
        }

        internal virtual void StopDrag()
        {
            onDragStop?.Invoke();
        }

        // CLICK
        internal void RegisterOnClick(Action action)
        {
            onClick += action;
        }

        internal virtual void ResolveClick()
        {
            if (!clicked)
            {
                DateTime click = DateTime.Now;
                TimeSpan span = click - lastClick;
                lastClick = click;
                Console.WriteLine(span.TotalMilliseconds);
                if (span.TotalMilliseconds < Engine.doubleClickTime)
                {
                    ResolveDoubleClick();
                    clicked = true;
                    return;
                }

                onClick?.Invoke();
            }
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
