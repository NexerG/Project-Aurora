using System.Numerics;
using ParticleSimulator.Forces;
using ParticleSimulator.ParticleTypes;

namespace ParticleSimulator.EngineWork
{
    internal struct StrctParticle
    {
        internal Vector3 point;
        internal Vector3 predPoint = new Vector3(); 
        internal Vector3 velocity = new Vector3();

        internal float radius = 7;

        internal Brush color = new Pen(Color.FromArgb(255, 255, 255, 255)).Brush;

        internal StrctParticle(Vector3 p)
        {
            point = p;
        }
    }

    public class Simulator3DStruct
    {
        //Frame
        Frame SC;
        Vector3 simSize;
        //particles and forces
        List<Force> forces = new List<Force>();
        Vector3 ConstForce = new Vector3();
        List<StrctParticle> strctParticles = new List<StrctParticle>();
        float[] densities;
        //Vars
        public float targetDensity = 0.00001f;
        public float smoothingRadius = 14f;
        public float pressureMultiplier = 3200f;
        public float viscosityStr = 15f;
        public float GravStrength = 1f;
        //Cells
        Entry[] SpatialLookup;
        int[] StartIndices;
        public Vector3[] Offsets2D = new Vector3[27];

        public Simulator3DStruct() //SPH algorithm
        {
            Gravity g = new Gravity(new PointF(0, 9.8f));
            forces.Add(g);

            //struct generation
            float offsetX = (700 / 2) - (15 * 7 / 2);
            float offsetY = (700 / 2) - (15 * 7 / 2);
            float offsetZ = (700 / 2) - (15 * 7 / 2);
            for (int i = 0; i < 15;i++)
            {
                for(int j=0; j<15; j++)
                {
                    for(int k=0; k<15; k++)
                    {
                        StrctParticle p = new StrctParticle();
                        p.point = new Vector3(i * 7 + offsetX, j * 7 + offsetY, k * 7 + offsetZ);
                        p.predPoint = new Vector3();
                        p.velocity = new Vector3();
                        p.radius = 7f;
                        p.color = new Pen(Color.FromArgb(255, 255, 255, 255)).Brush;

                        strctParticles.Add(p);
                    }
                }
            }

            SpatialLookup = new Entry[3375];
            StartIndices = new int[3375];
            densities = new float[3375];

            CreateOffsets();
            UpdateSpatialLookup(strctParticles, smoothingRadius);
            CalcConstForces();
            CreateOffsets();
            UpdateUI();
        }

        public Simulator3DStruct(Frame SC)
        {
            Gravity g = new Gravity(new Vector3(0, 0f, 9.8f));
            forces.Add(g);
            this.SC = SC;

            //struct generation
            float offsetX = (700 / 2) - (15 * 7 / 2);
            float offsetY = (700 / 2) - (15 * 7 / 2);
            float offsetZ = (700 / 2) - (15 * 7 / 2);
            for (int i = 0; i < 15; i++)
            {
                for (int j = 0; j < 15; j++)
                {
                    for (int k = 0; k < 15; k++)
                    {
                        StrctParticle p = new StrctParticle(new Vector3(i * 7 + offsetX, j * 7 + offsetY, k * 7 + offsetZ));
                        strctParticles.Add(p);
                    }
                }
            }

            SpatialLookup = new Entry[3375];
            StartIndices = new int[3375];
            densities = new float[3375];

            CreateOffsets();
            UpdateSpatialLookup(strctParticles, smoothingRadius);
            CalcConstForces();
            UpdateDensities();
            UpdateUI();
        }

        private void UpdateUI()
        {
            SC.TB_SmoothingRadius.Text = smoothingRadius.ToString();
            SC.TB_TargetDensity.Text = targetDensity.ToString();
            SC.TB_ViscosityStrength.Text = viscosityStr.ToString();
            SC.TB_PressureMult.Text = pressureMultiplier.ToString();
            SC.TB_GravStr.Text = GravStrength.ToString();
        }
        public void CreateOffsets()
        {
            //middle layer
            Offsets2D[0] = new Vector3(-1, 1, 0);
            Offsets2D[1] = new Vector3(0, 1, 0);
            Offsets2D[2] = new Vector3(1, 1, 0);
            Offsets2D[3] = new Vector3(-1, 0, 0);
            Offsets2D[4] = new Vector3(0, 0, 0);
            Offsets2D[5] = new Vector3(1, 0, 0);
            Offsets2D[6] = new Vector3(-1, -1, 0);
            Offsets2D[7] = new Vector3(0, -1, 0);
            Offsets2D[8] = new Vector3(1, -1, 0);
            //top layer
            Offsets2D[9] = new Vector3(-1, 1, 1);
            Offsets2D[10] = new Vector3(0, 1, 1);
            Offsets2D[11] = new Vector3(1, 1, 1);
            Offsets2D[12] = new Vector3(-1, 0, 1);
            Offsets2D[13] = new Vector3(0, 0, 1);
            Offsets2D[14] = new Vector3(1, 0, 1);
            Offsets2D[15] = new Vector3(-1, -1, 1);
            Offsets2D[16] = new Vector3(0, -1, 1);
            Offsets2D[17] = new Vector3(1, -1, 1);
            //bottom layer
            Offsets2D[18] = new Vector3(-1, 1, -1);
            Offsets2D[19] = new Vector3(0, 1, -1);
            Offsets2D[20] = new Vector3(1, 1, -1);
            Offsets2D[21] = new Vector3(-1, 0, -1);
            Offsets2D[22] = new Vector3(0, 0, -1);
            Offsets2D[23] = new Vector3(1, 0, -1);
            Offsets2D[24] = new Vector3(-1, -1, -1);
            Offsets2D[25] = new Vector3(0, -1, -1);
            Offsets2D[26] = new Vector3(1, -1, -1);
        }

        public void CalcConstForces()
        {
            foreach (Force f in forces)
            {
                ConstForce.X = ConstForce.X + f.force3.X;
                ConstForce.Y = ConstForce.Y + f.force3.Y;
                ConstForce.Z = ConstForce.Z + f.force3.Z;
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
            SimStep(TimeScale);
        }

        public void SimStep(float TimeScale)
        {
            //gravity
            Parallel.For(0, strctParticles.Count, i =>
            {
                StrctParticle tp = strctParticles[(int)i];
                tp.velocity += ConstForce * TimeScale * GravStrength;
                tp.predPoint = tp.point + tp.velocity;
                strctParticles[(int)i] = tp;
            });

            //assign points to cells
            UpdateSpatialLookup(strctParticles, smoothingRadius);
            //densities
            UpdateDensities();

            //////forces
            //pressure
            Parallel.For(0, strctParticles.Count, i =>
            {
                Vector3 pressureForce = CalcPresureForce(i);
                Vector3 pressureAccel = pressureForce / densities[i];

                StrctParticle tp = strctParticles[(int)i];
                tp.velocity += pressureAccel * TimeScale;
                strctParticles[(int)i] = tp;
            });

            //Viscosity
            Parallel.For(0, strctParticles.Count, i =>
            {
                Vector3 ViscForce = CalculateViscosityForce(i);

                StrctParticle tp = strctParticles[(int)i];
                tp.velocity += ViscForce * viscosityStr;
                strctParticles[(int)i] = tp;
            });
            UpdatePositions(strctParticles);
        }

        void UpdatePositions(List<StrctParticle> parts)
        {

            Parallel.For(0, parts.Count, i =>
            {
                if (parts[i].point.X + parts[i].radius + parts[i].velocity.X > simSize.Y)
                    parts[i].velocity.X = Math.Abs(parts[i].velocity.X) * -0.7f;

                if (parts[i].point.X + parts[i].radius + parts[i].velocity.X < 0)
                    parts[i].velocity.X = Math.Abs(parts[i].velocity.X) * 0.7f;

                if (parts[i].point.Y + parts[i].radius + parts[i].velocity.Y > simSize.X)
                    parts[i].velocity.Y = Math.Abs(parts[i].velocity.Y) * -0.7f;

                if (parts[i].point.Y + parts[i].radius + parts[i].velocity.Y < 0)
                    parts[i].velocity.Y = Math.Abs(parts[i].velocity.Y) * 0.7f;

                if (parts[i].point.Z + parts[i].radius + parts[i].velocity.Z > simSize.Z)
                    parts[i].velocity.Z = Math.Abs(parts[i].velocity.Z) * -0.7f;

                if (parts[i].point.Z + parts[i].radius + parts[i].velocity.Z < 0)
                    parts[i].velocity.Z = Math.Abs(parts[i].velocity.Z) * 0.7f;

                parts[i].point += parts[i].velocity;
            });
        }

        #region Density
        void UpdateDensities()
        {
            Parallel.For(0, parts.Count, i =>
            {
                densities[i] = CalculateDensity(parts[i].PredPoint);
            });
        }
        private float CalculateDensity(Vector3 samplePoint)
        {
            float density = 0;
            const float mass = 1;

            (int CenterX, int CenterY, int CenterZ) = PositionToCellCoord(samplePoint, smoothingRadius);
            float sqrRadius = smoothingRadius * smoothingRadius;

            foreach (Vector3 off in Offsets2D)
            {
                uint key = GetKeyFromHash(HashCell((int)(CenterX + off.X), (int)(CenterY + off.Y),(int)(CenterZ+off.Z)));
                int cellStartIndex = StartIndices[key];

                for (int i = cellStartIndex; i < SpatialLookup.Length; i++)
                {
                    if (SpatialLookup[i].CKey != key) break;

                    int particleIndex = SpatialLookup[i].index;
                    float sqrDist = (parts[particleIndex].PredPoint - samplePoint).LengthSquared();
                    if (sqrDist <= sqrRadius)
                    {
                        float dist = Vector3.Distance(samplePoint, parts[particleIndex].PredPoint);

                        float influence = SmoothingKernel(dist);
                        density += influence * mass;
                    }
                }
            }
            return density;
        }
        float SmoothingKernel(float dist)
        {
            if (dist >= smoothingRadius) return 0;

            float volume = (float)((Math.PI * Math.Pow(smoothingRadius, 4)) / 6);
            return (smoothingRadius - dist) * (smoothingRadius - dist) / volume;
        }
        #endregion

        #region Pressure
        Vector3 CalcPresureForce(int index)
        {
            Vector3 PressureForce = new Vector3(0, 0, 0);

            (int CenterX, int CenterY, int CenterZ) = PositionToCellCoord(parts[index].PredPoint, smoothingRadius);
            float sqrRadius = smoothingRadius * smoothingRadius;

            foreach (Vector3 off in Offsets2D)
            {
                uint key = GetKeyFromHash(HashCell((int)(CenterX + off.X), (int)(CenterY + off.Y), (int)(CenterZ + off.Z)));
                int cellStartIndex = StartIndices[key];

                for (int i = cellStartIndex; i < SpatialLookup.Length; i++)
                {
                    if (SpatialLookup[i].CKey != key) break;

                    int particleIndex = SpatialLookup[i].index;
                    float sqrDist = (parts[particleIndex].point - parts[index].point).LengthSquared();
                    if (sqrDist <= sqrRadius)
                    {
                        if (index == particleIndex) continue;
                        Vector3 offset = parts[particleIndex].PredPoint - parts[index].PredPoint;
                        float dist = offset.Length();
                        Vector3 dir = dist == 0 ? GetRandomDir() : offset / dist;

                        float slope = SmoothingKernelDerivative(dist);
                        float density = densities[particleIndex];
                        float sharedpressure = CalcSharedPressure(density, densities[index]);
                        PressureForce += sharedpressure * dir * slope * 1 / density;
                    }
                }
            }
            return PressureForce;
        }
        float SmoothingKernelDerivative(float dist)
        {
            if (dist >= smoothingRadius) return 0;

            float scale = (float)(12 / (Math.Pow(smoothingRadius, 4) * Math.PI));
            return (dist - smoothingRadius) * scale;
        }
        private float CalcSharedPressure(float DensA, float DensB)
        {
            float pressureA = ConvertDensityToPressure(DensA);
            float pressureB = ConvertDensityToPressure(DensB);
            return (pressureA + pressureB) / 2;
        }
        private Vector3 GetRandomDir()
        {
            Random r = new Random();
            return new Vector3(r.Next(-1, 1), r.Next(-1, 1), r.Next(-1,1));
        }
        float ConvertDensityToPressure(float density)
        {
            float densityError = density - targetDensity;
            float pressure = densityError * pressureMultiplier;
            return pressure;
        }
        #endregion

        #region Viscosity
        public Vector3 CalculateViscosityForce(int index)
        {
            Vector3 ViscosityForce = Vector3.Zero;

            (int CenterX, int CenterY, int CenterZ) = PositionToCellCoord(parts[index].point, smoothingRadius);
            float sqrRadius = smoothingRadius * smoothingRadius;

            foreach (Vector3 off in Offsets2D)
            {
                uint key = GetKeyFromHash(HashCell((int)(CenterX + off.X), (int)(CenterY + off.Y), (int)(CenterZ + off.Z)));
                int cellStartIndex = StartIndices[key];

                for (int i = cellStartIndex; i < SpatialLookup.Length; i++)
                {
                    if (SpatialLookup[i].CKey != key) break;

                    int particleIndex = SpatialLookup[i].index;
                    float sqrDist = (parts[particleIndex].point - parts[index].point).LengthSquared();
                    if (sqrDist <= sqrRadius)
                    {
                        if (index == particleIndex) continue;

                        float dist = Vector3.Distance(parts[index].point, parts[particleIndex].point);
                        float influence = ViscositySmoothingKernel(dist);
                        ViscosityForce += (parts[particleIndex].velocity - parts[index].velocity) * influence;
                    }
                }
            }
            return ViscosityForce;
        }
        private float ViscositySmoothingKernel(float dist)
        {
            if (dist >= smoothingRadius) return 0;

            float volume = (float)(Math.PI * Math.Pow(smoothingRadius, 8) / 4);
            float val = Math.Max(0, smoothingRadius * smoothingRadius - dist * dist);
            return val * val * val / volume;
        }
        #endregion

        #region Cells
        public void UpdateSpatialLookup(List<Particle3D> parts, float radius)
        {
            Parallel.For(0, parts.Count, i =>
            {
                (int cellX, int cellY, int cellZ) = PositionToCellCoord(parts[i], radius);
                uint CKey = GetKeyFromHash(HashCell(cellX, cellY, cellZ));
                SpatialLookup[i] = new Entry(i, CKey);
                StartIndices[i] = int.MaxValue;
            });

            Array.Sort(SpatialLookup);

            Parallel.For(0, parts.Count, i =>
            {
                uint key = SpatialLookup[i].CKey;
                uint KeyPrev = i == 0 ? int.MaxValue : SpatialLookup[i - 1].CKey;
                if (key != KeyPrev)
                {
                    StartIndices[key] = i;
                }
            });
        }
        private uint HashCell(int cellX, int cellY, int cellZ)
        {
            uint a = (uint)(cellX * 15823);
            uint b = (uint)(cellY * 9737333);
            return a + b;
        }
        private uint GetKeyFromHash(uint HC)
        {
            return HC % (uint)SpatialLookup.Length;
        }
        private (int cellX, int cellY, int cellZ) PositionToCellCoord(Particle3D particle, float radius)
        {
            int cellX = (int)(particle.PredPoint.X / radius);
            int cellY = (int)(particle.PredPoint.Y / radius);
            int cellZ = (int)(particle.PredPoint.Z / radius);
            return (cellX, cellY, cellZ);
        }
        private (int cellX, int cellY, int cellZ) PositionToCellCoord(Vector3 Point, float radius)
        {
            int cellX = (int)(Point.X / radius);
            int cellY = (int)(Point.Y / radius);
            int cellZ = (int)(Point.Z / radius);
            return (cellX, cellY, cellZ);
        }
        #endregion
    }
}