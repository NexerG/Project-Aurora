using ParticleSimulator.EngineWork.ComponentBehaviour;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace ParticleSimulator.GameObject
{
    internal class GameObject
    {
        //variables
        Vector3 Position = new Vector3(0,0,0);
        Vector3 Scale = new Vector3(1,1,1);
        Vector3 Rotation = new Vector3(0,0,0);

        List<ComponentBehaviour> _components = new List<ComponentBehaviour>();

        internal void OnStart()
        {
            foreach (ComponentBehaviour c in _components)
            {
                c.OnStart();
            }
        }

        internal void OnTick()
        {
            foreach(ComponentBehaviour c in _components)
            {
                c.OnTick();
            }
        }

        internal void OnDestroy()
        {
            foreach(ComponentBehaviour c in _components)
            {
                c.OnDestroy();
                _components.Remove(c);
            }
        }

        internal C CreateComponent<C>() where C : ComponentBehaviour, new()
        {
            C component = new C();
            _components.Add(component);
            return component;
        }

        internal C GetComponent<C>() where C : ComponentBehaviour
        {
            foreach(ComponentBehaviour comp in _components)
            {
                if (comp is C)
                    return (C)comp;
            }
            return null;
        }
    }
}
