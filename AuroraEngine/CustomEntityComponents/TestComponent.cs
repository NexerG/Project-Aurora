using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.EngineWork.ComponentBehaviour;

namespace ArctisAurora.CustomEntityComponents
{
    internal class TestComponent : EntityComponent
    {
        [@Serializable]
        public int testValue = 0;
    }
}