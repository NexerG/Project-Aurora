using System.Numerics;
using ParticleSimulator.Forces;
using ParticleSimulator.ParticleTypes;

namespace ParticleSimulator.EngineWork
{
    public class Simulator
    {
        Frame SC;

        List<Force> forces = new List<Force>();
        Vector2 ConstForce = new Vector2();
        List<Particle> parts;
        float[] densities;
        public float targetDensity = 2.75f;
        float smoothingRadius = 30f;
        float pressureMultiplier = 0.1f;

        public Simulator()
        {
            Gravity g = new Gravity(new PointF(0, 9.8f));
            forces.Add(g);
            CalcConstForces();
        }

        public Simulator(List<Particle> parts, Frame SC)
        {
            Gravity g = new Gravity(new PointF(0, 9.8f));
            forces.Add(g);
            this.parts = parts;
            this.SC = SC;

            densities= new float[parts.Count];
            CalcConstForces();
            UpdateDensities();
        }

        public void CalcConstForces()
        {
            foreach (Force f in forces)
            {
                ConstForce.X = ConstForce.X + f.force.X;
                ConstForce.Y = ConstForce.Y + f.force.Y;
            }
        }

        public void AddConstForce(Force f)
        {
            forces.Add(f);
            CalcConstForces();
        }
        public void AddConstForce(List<Force> fs)
        {
            forces.AddRange(fs);
            CalcConstForces();
        }

        public void Update(TimeSpan engineTime, float TimeScale)
        {
            //this.parts = ps;
            double TimeElapsed = engineTime.TotalMilliseconds / 1000f;

            Parallel.For(0, parts.Count, i =>
            {
                parts[i].velocity += ConstForce * TimeScale;
            });

            Parallel.For(0, parts.Count, i =>
            {
                Vector2 pressureFroce = CalcPresureForce(i);
                Vector2 pressureAccel = pressureFroce / densities[i];
                parts[i].velocity += -pressureAccel * TimeScale;
            });

            UpdatePositions(parts);
        }
        void UpdatePositions(List<Particle> parts)
        {
            Parallel.For(0, parts.Count, i =>
            {
                if (parts[i].point.X + parts[i].radius + parts[i].velocity.X > SC.PicBox.Width)
                    parts[i].velocity.X = Math.Abs(parts[i].velocity.X) * -0.7f;

                if (parts[i].point.X + parts[i].radius + parts[i].velocity.X < 0)
                    parts[i].velocity.X = Math.Abs(parts[i].velocity.X) * 0.7f;

                if (parts[i].point.Y + parts[i].radius + parts[i].velocity.Y > SC.PicBox.Height)
                    parts[i].velocity.Y = Math.Abs(parts[i].velocity.Y) * -0.7f;

                if (parts[i].point.Y + parts[i].radius + parts[i].velocity.Y < 0)
                    parts[i].velocity.Y = Math.Abs(parts[i].velocity.Y) * 0.7f;

                parts[i].point += parts[i].velocity;
            });
        }

        void UpdateDensities()
        {
            Parallel.For(0, parts.Count, i =>
            {
                densities[i] = CalculateDensity(parts[i].point);
            });
        }

        private float CalculateDensity(Vector2 samplePoint)
        {
            float density = 0;
            const float mass = 1;

            foreach (Particle p in parts)
            {
                float dist = Vector2.Distance(samplePoint, p.point);
                float influence = SmoothingKernel(smoothingRadius, dist);
                density += influence;
            }
            return density;
        }
        float SmoothingKernel(float radius, float dist)
        {
            if (dist >= radius) return 0;

            float volume = (float)((Math.PI * Math.Pow(radius, 4)) / 6);
            return (radius - dist) * (radius - dist) / volume;
        }

        static float SmoothingKernelDerivative(float dist, float radius)
        {
            if (dist >= radius) return 0;

            float scale = (float)(13 / (Math.Pow(radius, 4) * Math.PI));
            return (dist - radius) * scale;
        }

        Vector2 CalcPresureForce(int index)
        {
            Vector2 PressureForce = Vector2.Zero;
            for (int i = 0; i < parts.Count; i++)
            {
                if (index == i) continue;
                Vector2 offset = parts[i].point - parts[index].point;
                float dist = offset.Length();
                Vector2 dir = dist == 0 ? GetRandomDir() : offset / dist;

                float slope = SmoothingKernelDerivative(dist, smoothingRadius);
                float density = densities[i];
                float sharedpressure = CalcSharedPressure(density, densities[i]);
                PressureForce += sharedpressure * dir * slope * 1 / density;
            }
            return PressureForce;
        }

        private float CalcSharedPressure(float DensA, float DensB)
        {
            float pressureA = ConverDesntiyToPresure(DensA);
            float pressureB = ConverDesntiyToPresure(DensB);
            return (pressureA + pressureB) / 2;
        }

        private Vector2 GetRandomDir()
        {
            Random r = new Random();
            return new Vector2(r.Next(0, 1), r.Next(0, 1));
        }

        float ConverDesntiyToPresure(float density)
        {
            float densityError = density - targetDensity;
            float pressure = densityError * pressureMultiplier;
            return pressure;
        }
    }
}