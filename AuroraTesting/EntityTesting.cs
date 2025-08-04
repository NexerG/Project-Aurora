using ArctisAurora.CustomEntities;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.ComponentBehaviour;
using ArctisAurora.EngineWork.EngineEntity;
using ArctisAurora.ParticleTypes;
using ArctisAurora.Simulators.Vulkan;
using Silk.NET.Maths;
using System.Drawing.Drawing2D;

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

        [TestMethod]
        public void CreateComponent_AddsNewComponentToEntity()
        {
            // Arrange
            Entity entity = new Entity();

            // Act
            EntityComponent component = entity.CreateComponent<SampleComponent>();

            // Assert
            Assert.IsNotNull(component, "Component should be created and added to the entity.");
            Assert.IsTrue(entity._components.Contains(component), "Component list should contain the new component.");
            Assert.AreEqual(entity, component.parent, "Component's parent should be the entity.");
        }

        [TestMethod]
        public void GetComponent_ReturnsCorrectComponentIfExists()
        {
            // Arrange
            Entity entity = new Entity();
            var addedComponent = entity.CreateComponent<SampleComponent>();

            // Act
            var retrievedComponent = entity.GetComponent<SampleComponent>();

            // Assert
            Assert.IsNotNull(retrievedComponent, "GetComponent should return the added component.");
            Assert.AreSame(addedComponent, retrievedComponent, "Retrieved component should match the added component.");
        }

        [TestMethod]
        public void GetChildEntityByName_FindsCorrectChildEntity()
        {
            // Arrange
            Entity parentEntity = new Entity();
            var childEntity = new Entity("ChildEntity");
            parentEntity._children.Add(childEntity);

            // Act
            var foundEntity = parentEntity.GetChildEntityByName("ChildEntity");

            // Assert
            Assert.IsNotNull(foundEntity, "Should find the child entity by name.");
            Assert.AreEqual("ChildEntity", foundEntity.name, "The found entity should have the correct name.");
        }

        [TestMethod]
        public void RemoveComponent_RemovesSpecifiedComponent()
        {
            // Arrange
            Entity entity = new Entity();
            var component = entity.CreateComponent<SampleComponent>();

            // Act
            var removedComponent = entity.RemoveComponent<SampleComponent>();

            // Assert
            Assert.IsNull(entity.GetComponent<SampleComponent>(), "Component should no longer be in the entity's component list.");
            Assert.IsFalse(entity._components.Contains(component), "Component list should not contain the removed component.");
        }

        [TestMethod]
        public void TestSimulator()
        {
            //setup
            int particleRoot = 22;
            List<Particle3D> _particles = new List<Particle3D>();
            float offsetX = (700 / 2) - (particleRoot * 7 / 2);
            float offsetY = (700 / 2) - (particleRoot * 7 / 2);
            float offsetZ = (700 / 2) - (particleRoot * 7 / 2);
            for (int i = 0; i < particleRoot; i++)
            {
                for (int j = 0; j < particleRoot; j++)
                {
                    for (int k = 0; k < particleRoot; k++)
                    {
                        _particles.Add(new Particle3D((i * 7 + offsetX), (j * 7 + offsetY), k * 7 + offsetZ));
                    }
                }
            }
            Simulator3D _simulator = new Simulator3D(_particles, new Vector3D<float>(700, 700, 700));

            List<float> _times = new List<float>();
            //DateTime _stepTimeStart, _stepTimeEnd;
            for (int i = 0; i < 100; i++)
            {
                //_stepTimeStart = DateTime.Now;
                _simulator.Update(8f / 1000f);
                //_stepTimeEnd = DateTime.Now;

                //TimeSpan _stepTime = _stepTimeStart - _stepTimeEnd;
                //_times.Add((float)_stepTime.TotalMilliseconds);
            }
            //double _totalTestTime = _times.Sum()/1000;
        }
    }

    // Mock classes for testing
    public class SampleComponent : EntityComponent
    {
        public Entity parent;
        public void OnStart() { }
        public void OnEnable() { }
        public void OnDisable() { }
        public void OnTick() { }
        public void OnDestroy() { }
    }
}