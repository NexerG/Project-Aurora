using System.Numerics;

namespace ArctisAurora.Core.Animation
{
    // Eases a target's properties between rest, hover and press values as its interaction changes.
    public sealed class StateBinding
    {
        private readonly SignalHandle _signal;
        private readonly AnimationHandle[] _springs;

        private StateBinding(object target, BindingDefinition definition, float frequency, float damping)
        {
            _signal = Signals.Create();
            _springs = new AnimationHandle[definition.tracks.Count];
            for (int i = 0; i < _springs.Length; i++)
            {
                BindingTrackDefinition authored = definition.tracks[i];
                Vector4 rest = authored.rest == null ? AnimatableProperty.Of(target.GetType(), authored.property).get(target) : AnimationLibrary.ParseValue(authored.rest);
                Vector4 hover = authored.hover == null ? rest : AnimationLibrary.ParseValue(authored.hover);
                Vector4 press = authored.press == null ? hover : AnimationLibrary.ParseValue(authored.press);
                _springs[i] = Animations.Spring(target, authored.property,
                    definition.frequency >= 0f ? definition.frequency : frequency,
                    definition.damping >= 0f ? definition.damping : damping, _signal, rest, hover, press);
            }
        }

        // frequency and damping are used where the binding leaves them out.
        public static StateBinding Attach(object target, BindingDefinition definition, float frequency, float damping)
            => new StateBinding(target, definition, frequency, damping);

        public void Set(bool hovered, bool pressed)
            => Signals.Set(_signal, new Vector4(pressed ? 2f : hovered ? 1f : 0f, 0f, 0f, 0f));

        public void Detach()
        {
            foreach (AnimationHandle spring in _springs) Animations.Stop(spring);
            Signals.Release(_signal);
        }
    }
}
