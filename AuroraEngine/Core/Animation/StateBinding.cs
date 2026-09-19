using System.Numerics;

namespace ArctisAurora.Core.Animation
{
    // Eases a target's properties between rest, hover and press values as its interaction changes.
    public sealed class StateBinding
    {
        private sealed class Track
        {
            public AnimatableProperty property = null!;
            public Vector4 rest;
            public Vector4 hover;
            public Vector4 press;
        }

        private readonly object _target;
        private readonly Track[] _tracks;
        private readonly SignalHandle _signal;
        private readonly AnimationHandle _spring;

        // 0 rest, 1 hover, 2 press, eased between by the spring.
        [A_Animatable]
        public float state
        {
            get => field;
            set
            {
                field = value;
                Apply();
            }
        }

        private StateBinding(object target, BindingDefinition definition, float frequency, float damping)
        {
            _target = target;
            _tracks = new Track[definition.tracks.Count];
            for (int i = 0; i < _tracks.Length; i++)
            {
                BindingTrackDefinition authored = definition.tracks[i];
                Track track = new Track { property = AnimatableProperty.Of(target.GetType(), authored.property) };
                track.rest = authored.rest == null ? track.property.get(target) : AnimationLibrary.ParseValue(authored.rest);
                track.hover = authored.hover == null ? track.rest : AnimationLibrary.ParseValue(authored.hover);
                track.press = authored.press == null ? track.hover : AnimationLibrary.ParseValue(authored.press);
                _tracks[i] = track;
            }

            _signal = Signals.Create();
            _spring = Animations.Spring(this, nameof(state),
                definition.frequency >= 0f ? definition.frequency : frequency,
                definition.damping >= 0f ? definition.damping : damping, _signal);
        }

        // frequency and damping are used where the binding leaves them out.
        public static StateBinding Attach(object target, BindingDefinition definition, float frequency, float damping)
            => new StateBinding(target, definition, frequency, damping);

        public void Set(bool hovered, bool pressed)
            => Signals.Set(_signal, new Vector4(pressed ? 2f : hovered ? 1f : 0f, 0f, 0f, 0f));

        public void Detach()
        {
            Animations.Stop(_spring);
            Signals.Release(_signal);
        }

        private void Apply()
        {
            float s = state;
            foreach (Track track in _tracks)
            {
                Vector4 value = s <= 1f ? Vector4.Lerp(track.rest, track.hover, s) : Vector4.Lerp(track.hover, track.press, s - 1f);
                track.property.set(_target, value);
            }
        }
    }
}
