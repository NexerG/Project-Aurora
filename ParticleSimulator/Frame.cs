using ArctisAurora.EngineWork;

namespace ArctisAurora
{
    public partial class Frame : Form
    {
        internal Engine engine = null;
        bool is3D = true;
        int parts = 0;

        public Frame()
        {
            //initialization
            InitializeComponentBehaviour();
        }

        private void TestButton_Click(object sender, EventArgs e)
        {

        }

        private void TB_SmoothingRadius_Validated(object sender, EventArgs e)
        {
        }

        private void TB_TargetDensity_Validated(object sender, EventArgs e)
        {
        }

        private void TB_PressureMult_Validated(object sender, EventArgs e)
        {
        }

        private void TB_ViscosityStrength_Validated(object sender, EventArgs e)
        {
        }

        private void TB_GravStr_Validated(object sender, EventArgs e)
        {
        }

        private void Frame_Load(object sender, EventArgs e)
        {
            //GLControl.Paint += GLControl_Paint;
            //GLControl.Resize += GLControl_Resize;
        }
        public void GLControl_Resize(object? sender, EventArgs e)
        {
            //GLControl.MakeCurrent();
            //GL.Viewport(0, 0, GLControl.Width, GLControl.Height);
        }

        private void GLControl_Paint(object? sender, PaintEventArgs e)
        {
            //GLControl.MakeCurrent();
            //GL.ClearColor(Color.FromArgb(255, 30, 30, 30));
            //GL.Clear(ClearBufferMask.ColorBufferBit);
            //GLControl.SwapBuffers();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            //GLControl.Invalidate();
        }

        private void Frame_FormClosing(object sender, FormClosingEventArgs e)
        {

        }

        private void GLControl_MouseDown(object sender, MouseEventArgs e)
        {
            /*if (engine != null)
                engine.MouseHandler(e, 1);*/
        }

        private void GLControl_MouseUp(object sender, MouseEventArgs e)
        {
            /*if (engine != null)
                engine.MouseHandler(e, 0);*/
        }

        private void GLControl_MouseMove(object sender, MouseEventArgs e)
        {
            /*if (engine != null)
                engine.MouseHandler(e, 2);*/
        }

        private void GLControl_KeyDown(object sender, KeyEventArgs e)
        {

        }

        private void GLControl_MouseClick(object sender, MouseEventArgs e)
        {
            //GLControl.Focus();
            Console.WriteLine("focus");
        }

        private void Frame_KeyDown(object sender, KeyEventArgs e)
        {
            //engine.KeyboardHandler(e);
        }

        private void GLControl_KeyPress(object sender, KeyPressEventArgs e)
        {

        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            is3D = checkBox1.Checked;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (engine == null)
            {
                engine = new Engine();
                engine.Init(this);
            }
        }

        private void TB_ParticleAmount_Validated(object sender, EventArgs e)
        {
            parts = int.Parse(TB_ParticleAmount.Text);
        }

        private void TB_ParticleAmount_Validating(object sender, EventArgs e)
        {
            parts = int.Parse(TB_ParticleAmount.Text);
        }
    }
}