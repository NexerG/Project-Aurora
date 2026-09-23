using ArctisAurora.Core.Registry;

namespace ArctisAurora.Core.Threading
{
    // Fixed-rate simulation, stepped after entity logic through the transforms both write.
    //
    // The step is empty: this is still the placeholder the old PhysicsThread was, just no longer
    // pretending to hand off to anyone.
    [A_XSDType("Physics", "Systems")]
    public sealed class PhysicsSystem : ThreadedSystem
    {
        protected override double TargetPeriodMs => 32.0;

        [A_XSDActionDependency("Physics.Step", "Frame")]
        private void Simulate()
        {
        }
    }
}
