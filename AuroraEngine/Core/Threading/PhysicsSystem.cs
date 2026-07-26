using ArctisAurora.Core.Registry;

namespace ArctisAurora.Core.Threading
{
    // Fixed-rate simulation. Nothing waits on it and it waits on nothing — it owns its tables and
    // writes them in place, and anything it needs from another system arrives as a command.
    //
    // The tick is empty: this is still the placeholder the old PhysicsThread was, just no longer
    // pretending to hand off to anyone.
    [A_XSDType("Physics", "Systems")]
    public sealed class PhysicsSystem : ThreadedSystem
    {
        protected override double TargetPeriodMs => 32.0;

        protected override void Tick()
        {
        }
    }
}
