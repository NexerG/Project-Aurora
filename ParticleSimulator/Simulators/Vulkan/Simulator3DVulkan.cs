using ArctisAurora.Forces;
using ArctisAurora.ParticleTypes;
using Silk.NET.Maths;

namespace ArctisAurora.Simulators.Vulkan
{
    public class Simulator3DVulkan
    {
        //Frame
        Frame SC;
        bool simOpenTK = true;
        Vector3D<float> simSize;
        //particles and forces
        List<Force> forces = new List<Force>();
        Vector3D<float> ConstForce = new Vector3D<float>();
        List<Particle3DVulkan> parts;
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
        public Vector3D<float>[] Offsets2D = new Vector3D<float>[27];

        public Simulator3DVulkan() //SPH algorithm
        {
            Gravity g = new Gravity(new PointF(0, 9.8f));
            forces.Add(g);

            SpatialLookup = new Entry[parts.Count];
            StartIndices = new int[parts.Count];
            densities = new float[parts.Count];

            CreateOffsets();
            UpdateSpatialLookup(parts, smoothingRadius);
            CalcConstForces();
            CreateOffsets();
            UpdateUI();
        }

        public Simulator3DVulkan(List<Particle3DVulkan> parts, Frame SC)
        {
            Gravity g = new Gravity(new Vector3D<float>(0f, 0f, 9.8f));
            forces.Add(g);
            this.parts = parts;
            this.SC = SC;

            SpatialLookup = new Entry[parts.Count];
            StartIndices = new int[parts.Count];
            densities = new float[parts.Count];

            CreateOffsets();
            UpdateSpatialLookup(parts, smoothingRadius);
            CalcConstForces();
            UpdateDensities();
            UpdateUI();
        }

        public Simulator3DVulkan(Frame frame, List<Particle3DVulkan> parts, Vector3D<float> simSize)
        {
            SC = frame;
            Gravity g = new Gravity(new Vector3D<float>(0f, 9.8f, 0f));
            forces.Add(g);
            this.parts = parts;

            SpatialLookup = new Entry[parts.Count];
            StartIndices = new int[parts.Count];
            densities = new float[parts.Count];
            this.simSize = simSize;

            CreateOffsets();
            UpdateSpatialLookup(parts, smoothingRadius);
            CalcConstForces();
            UpdateDensities();
            UpdateUI();
        }

        public Simulator3DVulkan(List<Particle3DVulkan> parts, Vector3D<float> simSize)
        {
            Gravity g = new Gravity(new Vector3D<float>(0f, -9.8f, 0f));
            forces.Add(g);
            this.parts = parts;

            SpatialLookup = new Entry[parts.Count];
            StartIndices = new int[parts.Count];
            densities = new float[parts.Count];
            this.simSize = simSize;

            CreateOffsets();
            UpdateSpatialLookup(parts, smoothingRadius);
            CalcConstForces();
            UpdateDensities();
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
            Offsets2D[0] = new Vector3D<float>(-1, 1, 0);
            Offsets2D[1] = new Vector3D<float>(0, 1, 0);
            Offsets2D[2] = new Vector3D<float>(1, 1, 0);
            Offsets2D[3] = new Vector3D<float>(-1, 0, 0);
            Offsets2D[4] = new Vector3D<float>(0, 0, 0);
            Offsets2D[5] = new Vector3D<float>(1, 0, 0);
            Offsets2D[6] = new Vector3D<float>(-1, -1, 0);
            Offsets2D[7] = new Vector3D<float>(0, -1, 0);
            Offsets2D[8] = new Vector3D<float>(1, -1, 0);
            //top layer
            Offsets2D[9] = new Vector3D<float>(-1, 1, 1);
            Offsets2D[10] = new Vector3D<float>(0, 1, 1);
            Offsets2D[11] = new Vector3D<float>(1, 1, 1);
            Offsets2D[12] = new Vector3D<float>(-1, 0, 1);
            Offsets2D[13] = new Vector3D<float>(0, 0, 1);
            Offsets2D[14] = new Vector3D<float>(1, 0, 1);
            Offsets2D[15] = new Vector3D<float>(-1, -1, 1);
            Offsets2D[16] = new Vector3D<float>(0, -1, 1);
            Offsets2D[17] = new Vector3D<float>(1, -1, 1);
            //bottom layer
            Offsets2D[18] = new Vector3D<float>(-1, 1, -1);
            Offsets2D[19] = new Vector3D<float>(0, 1, -1);
            Offsets2D[20] = new Vector3D<float>(1, 1, -1);
            Offsets2D[21] = new Vector3D<float>(-1, 0, -1);
            Offsets2D[22] = new Vector3D<float>(0, 0, -1);
            Offsets2D[23] = new Vector3D<float>(1, 0, -1);
            Offsets2D[24] = new Vector3D<float>(-1, -1, -1);
            Offsets2D[25] = new Vector3D<float>(0, -1, -1);
            Offsets2D[26] = new Vector3D<float>(1, -1, -1);
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

        public void Update(float TimeScale)
        {
            SimStep(TimeScale);
        }

        public void SimStep(float TimeScale)
        {
            //gravity
            Parallel.For(0, parts.Count, i =>
            {
                parts[i].velocity += ConstForce * TimeScale * GravStrength;
                parts[i].PredPoint = parts[i].point + parts[i].velocity;
            });

            //assign points to cells
            UpdateSpatialLookup(parts, smoothingRadius);
            //densities
            UpdateDensities();

            //////forces
            //pressure
            Parallel.For(0, parts.Count, i =>
            {
                Vector3D<float> pressureForce = CalcPresureForce(i);
                Vector3D<float> pressureAccel = pressureForce / densities[i];
                parts[i].velocity += pressureAccel * TimeScale;
            });

            //Viscosity
            Parallel.For(0, parts.Count, i =>
            {
                Vector3D<float> ViscForce = CalculateViscosityForce(i);
                parts[i].velocity += ViscForce * viscosityStr;
            });
            UpdatePositions(parts);
        }

        void UpdatePositions(List<Particle3DVulkan> parts)
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

        private float CalculateDensity(Vector3D<float> samplePoint)
        {
            float density = 0;
            const float mass = 1;

            (int CenterX, int CenterY, int CenterZ) = PositionToCellCoord(samplePoint, smoothingRadius);
            float sqrRadius = smoothingRadius * smoothingRadius;

            foreach (Vector3D<float> off in Offsets2D)
            {
                uint key = GetKeyFromHash(HashCell((int)(CenterX + off.X), (int)(CenterY + off.Y), (int)(CenterZ + off.Z)));
                int cellStartIndex = StartIndices[key];

                for (int i = cellStartIndex; i < SpatialLookup.Length; i++)
                {
                    if (SpatialLookup[i].CKey != key) break;

                    int particleIndex = SpatialLookup[i].index;
                    float sqrDist = (parts[particleIndex].PredPoint - samplePoint).LengthSquared;
                    if (sqrDist <= sqrRadius)
                    {
                        float dist = Vector3D.Distance(samplePoint, parts[particleIndex].PredPoint);

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

            float volume = (float)(Math.PI * Math.Pow(smoothingRadius, 4) / 6);
            return (smoothingRadius - dist) * (smoothingRadius - dist) / volume;
        }
        #endregion

        #region Pressure
        Vector3D<float> CalcPresureForce(int index)
        {
            Vector3D<float> PressureForce = new Vector3D<float>(0, 0, 0);

            (int CenterX, int CenterY, int CenterZ) = PositionToCellCoord(parts[index].PredPoint, smoothingRadius);
            float sqrRadius = smoothingRadius * smoothingRadius;

            foreach (Vector3D<float> off in Offsets2D)
            {
                uint key = GetKeyFromHash(HashCell((int)(CenterX + off.X), (int)(CenterY + off.Y), (int)(CenterZ + off.Z)));
                int cellStartIndex = StartIndices[key];

                for (int i = cellStartIndex; i < SpatialLookup.Length; i++)
                {
                    if (SpatialLookup[i].CKey != key) break;

                    int particleIndex = SpatialLookup[i].index;
                    float sqrDist = (parts[particleIndex].point - parts[index].point).LengthSquared;
                    if (sqrDist <= sqrRadius)
                    {
                        if (index == particleIndex) continue;
                        Vector3D<float> offset = parts[particleIndex].PredPoint - parts[index].PredPoint;
                        float dist = offset.Length;
                        Vector3D<float> dir = dist == 0 ? GetRandomDir() : offset / dist;

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
        private Vector3D<float> GetRandomDir()
        {
            Random r = new Random();
            return new Vector3D<float>(r.Next(-1, 1), r.Next(-1, 1), r.Next(-1, 1));
        }
        float ConvertDensityToPressure(float density)
        {
            float densityError = density - targetDensity;
            float pressure = densityError * pressureMultiplier;
            return pressure;
        }
        #endregion

        #region Viscosity
        public Vector3D<float> CalculateViscosityForce(int index)
        {
            Vector3D<float> ViscosityForce = Vector3D<float>.Zero;

            (int CenterX, int CenterY, int CenterZ) = PositionToCellCoord(parts[index].point, smoothingRadius);
            float sqrRadius = smoothingRadius * smoothingRadius;

            foreach (Vector3D<float> off in Offsets2D)
            {
                uint key = GetKeyFromHash(HashCell((int)(CenterX + off.X), (int)(CenterY + off.Y), (int)(CenterZ + off.Z)));
                int cellStartIndex = StartIndices[key];

                for (int i = cellStartIndex; i < SpatialLookup.Length; i++)
                {
                    if (SpatialLookup[i].CKey != key) break;

                    int particleIndex = SpatialLookup[i].index;
                    float sqrDist = (parts[particleIndex].point - parts[index].point).LengthSquared;
                    if (sqrDist <= sqrRadius)
                    {
                        if (index == particleIndex) continue;

                        float dist = Vector3D.Distance(parts[index].point, parts[particleIndex].point);
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
        public void UpdateSpatialLookup(List<Particle3DVulkan> parts, float radius)
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
        private (int cellX, int cellY, int cellZ) PositionToCellCoord(Particle3DVulkan particle, float radius)
        {
            int cellX = (int)(particle.PredPoint.X / radius);
            int cellY = (int)(particle.PredPoint.Y / radius);
            int cellZ = (int)(particle.PredPoint.Z / radius);
            return (cellX, cellY, cellZ);
        }
        private (int cellX, int cellY, int cellZ) PositionToCellCoord(Vector3D<float> Point, float radius)
        {
            int cellX = (int)(Point.X / radius);
            int cellY = (int)(Point.Y / radius);
            int cellZ = (int)(Point.Z / radius);
            return (cellX, cellY, cellZ);
        }
        #endregion
    }
}