namespace ParticleSimulator
{
    public partial class Frame : Form
    {
        Engine engine = null;

        public Frame()
        {
            //initialization
            InitializeComponent();
            Engine engine = new Engine();
            engine.Init(this);
        }
    }
}