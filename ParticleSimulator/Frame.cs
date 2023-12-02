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
            int RowofParts = (int)Math.Sqrt(engine.particles.Count);
            float offsetX = (PicBox.Width / 2) - (RowofParts * 7 / 2);
            float offsetY = (PicBox.Height / 2) - (RowofParts * 7 / 2);
            for (int i = 0; i < RowofParts; i++)
            {
                for (int j = 0; j < RowofParts; j++)
                {
                    engine.particles[i * RowofParts + j].velocity = new Vector2(0, 0);
                    engine.particles[i * RowofParts + j].point = new Vector2(i * 7 + offsetX, j * 7 + offsetY);
                }
            }
        }

        private void TB_SmoothingRadius_Validated(object sender, EventArgs e)
        {
            engine.simulator.smoothingRadius = float.Parse(TB_SmoothingRadius.Text);
        }

        private void TB_TargetDensity_Validated(object sender, EventArgs e)
        {
            engine.simulator.targetDensity = float.Parse(TB_TargetDensity.Text);
        }

        private void TB_PressureMult_Validated(object sender, EventArgs e)
        {
            engine.simulator.pressureMultiplier = float.Parse(TB_PressureMult.Text);
        }

        private void TB_ViscosityStrength_Validated(object sender, EventArgs e)
        {
            engine.simulator.viscosityStr = float.Parse(TB_ViscosityStrength.Text);
        }

        private void TB_GravStr_Validated(object sender, EventArgs e)
        {
            engine.simulator.GravStrength = float.Parse(TB_GravStr.Text);
        }
    }
}