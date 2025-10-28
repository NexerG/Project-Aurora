using ArctisAurora.EngineWork.ComponentBehaviour;
using ArctisAurora.EngineWork.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArctisAurora.CustomEntityComponents
{
    internal class TestComponent : EntityComponent
    {
        [@Serializable]
        public int testValue = 0;
    }
}