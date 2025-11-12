using ArctisAurora.EngineWork.ComponentBehaviour;
using ArctisAurora.EngineWork.Serialization;

namespace ArctisAurora.CustomEntityComponents
{
    internal class TestComponent : EntityComponent
    {
        [@Serializable]
        public int testValue = 0;
    }
}