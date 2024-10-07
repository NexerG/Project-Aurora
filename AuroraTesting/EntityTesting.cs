using ArctisAurora.CustomEntities;
using ArctisAurora.EngineWork;
using Silk.NET.Maths;

namespace AuroraTesting
{
    [TestClass]
    public class EntityTesting
    {
        [TestMethod]
        public void SpawnEntity()
        {
            //Engine engine = new Engine();
            TestingEntity _te = new TestingEntity();
            _te.transform.SetWorldScale(new Vector3D<float>(50, 1, 50));
            _te.transform.SetWorldPosition(new Vector3D<float>(0, -5, 0));
            
            Assert.IsTrue(true);
        }
    }
}