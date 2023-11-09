namespace ParticleSimulator
{
    public partial class Form1 : Form
    {
        public List<Particle> Particles { get; }
        Bitmap bmp;
        Graphics g;

        public Form1(List<Particle> particles)
        {
            //initialization
            InitializeComponent();
            
            //picture stuff
            bmp = new Bitmap(PicBox.Width, PicBox.Height);
            g = Graphics.FromImage(bmp);
            
            //data
            Particles = particles;
        }

        private void TestButton_Click(object sender, EventArgs e)
        {
            Particle testas= new Particle(50,50);
            Particles.Add(testas);
            Draw(Particles);
        }

        public void Draw(List<Particle> p)
        {
            g.Clear(Color.FromArgb(255, 30, 30, 30));
            PicBox.Image = bmp;
            
            //drawing
            foreach(Particle particle in p)
            {
                g.DrawRectangle(Pens.White, particle.point.X, particle.point.Y, 10, 10);
            }
            PicBox.Invalidate();
        }

        private void Form1_Resize(object sender, EventArgs e)
        {
            bmp = new Bitmap(PicBox.Width, PicBox.Height);
            PicBox.Image = bmp;
            g = Graphics.FromImage(bmp);
            Draw(Particles);
        }
    }
}