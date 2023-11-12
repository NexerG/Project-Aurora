using ParticleSimulator.EngineWork;
using System.Numerics;

namespace ParticleSimulator
{
    public partial class Frame : Form
    {
        Engine engine = null;

        public Frame()
        {
            //initialization
            InitializeComponent();
            engine = new Engine();
            engine.Init(this);
        }

        private void TestButton_Click(object sender, EventArgs e)
        {
            engine.particles[0].velocity = new Vector2(0, 0);
            engine.particles[0].point = new Vector2(100, 400);

        }
    }
}