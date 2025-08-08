using Silk.NET.Maths;
using Silk.NET.Vulkan;

namespace ArctisAurora.EngineWork.Rendering.UI.Controls.Interactable
{
    internal abstract class InteractableControl : PanelControl
    {
        public event Action onEnter;
        public event Action onExit;
        public event Action onClick;
        public event Action onRelease;
        public event Action<VulkanControl, Vector2D<float>> onDrag;

        private bool entered = false;
        private bool clicked = false;

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
        internal void RegisterOnDrag(Action<VulkanControl, Vector2D<float>> action)
        {
            onDrag += action;
        }

        internal virtual void ResolverDrag(VulkanControl control, Vector2D<float> pos)
        {
            onDrag?.Invoke(control, pos);
        }

        // CLICK
        internal void RegisterOnClick(Action action)
        {
            onClick += action;
        }

        internal virtual void ResolveClick()
        {
            onClick?.Invoke();
            clicked = true;
        }

        // RELEASE
        internal void RegisterOnRelease(Action action)
        {
            onRelease += action;
        }

        internal virtual void ResolveRelease()
        {
            onRelease?.Invoke();
            clicked = false;
        }
    }
}
