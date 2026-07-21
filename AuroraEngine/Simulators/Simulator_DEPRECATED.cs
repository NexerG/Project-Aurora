using System;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ArctisAurora.Forces;
using ArctisAurora.Simulators.ParticleTypes;

namespace ArctisAurora.Simulators
{
    public class Entry : IComparable, IComparable<Entry>
    {
        public int index;
        public uint CKey;
        public Entry(int i, uint ck)
        {
            index = i;
            CKey = ck;
        }

        public int CompareTo(Entry other)
        {
            return CKey.CompareTo(other.CKey);
        }

        public int CompareTo(object? obj)
        {
            return CKey.CompareTo(obj);
        }
    }

    public class Simulator_DEPRECATED
    {
        //Frame
        Frame SC;
        Vector2 simSize;
        //particles and forces
        List<Force> forces = new List<Force>();
        Vector2 ConstForce = new Vector2();
        List<Particle2D> parts;
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
        public Vector2[] Offsets2D = new Vector2[9];

        public Simulator_DEPRECATED() //SPH algorithm
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

        public Simulator_DEPRECATED(List<Particle2D> parts, Frame SC)
        {
            Gravity g = new Gravity(new PointF(0, 9.8f));
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

        public Simulator_DEPRECATED(Frame frame, List<Particle2D> parts, Vector2 simSize)
        {
            SC = frame;
            Gravity g = new Gravity(new PointF(0, 9.8f));
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

        private void UpdateUI()
        {
        }
        public void CreateOffsets()
        {
            Offsets2D[0] = new Vector2(-1, 1);
            Offsets2D[1] = new Vector2(0, 1);
            Offsets2D[2] = new Vector2(1, 1);
            Offsets2D[3] = new Vector2(-1, 0);
            Offsets2D[4] = new Vector2(0, 0);
            Offsets2D[5] = new Vector2(1, 0);
            Offsets2D[6] = new Vector2(-1, -1);
            Offsets2D[7] = new Vector2(0, -1);
            Offsets2D[8] = new Vector2(1, -1);
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
                Vector2 pressureForce = CalcPresureForce(i);
                Vector2 pressureAccel = pressureForce / densities[i];
                parts[i].velocity += pressureAccel * TimeScale;
            });

            //Viscosity
            Parallel.For(0, parts.Count, i =>
            {
                Vector2 ViscForce = CalculateViscosityForce(i);
                parts[i].velocity += ViscForce * viscosityStr;
            });
            UpdatePositions(parts);
        }

        void UpdatePositions(List<Particle2D> parts)
        {
            Parallel.For(0, parts.Count, i =>
            {
                if (parts[i].point.X + parts[i].radius + parts[i].velocity.X > simSize.X)
                    parts[i].velocity.X = Math.Abs(parts[i].velocity.X) * -0.7f;

                if (parts[i].point.X + parts[i].radius + parts[i].velocity.X < 0)
                    parts[i].velocity.X = Math.Abs(parts[i].velocity.X) * 0.7f;

                if (parts[i].point.Y + parts[i].radius + parts[i].velocity.Y > simSize.Y)
                    parts[i].velocity.Y = Math.Abs(parts[i].velocity.Y) * -0.7f;

                if (parts[i].point.Y + parts[i].radius + parts[i].velocity.Y < 0)
                    parts[i].velocity.Y = Math.Abs(parts[i].velocity.Y) * 0.7f;

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
        private float CalculateDensity(Vector2 samplePoint)
        {
            float density = 0;
            const float mass = 1;

            (int CenterX, int CenterY) = PositionToCellCoord(samplePoint, smoothingRadius);
            float sqrRadius = smoothingRadius * smoothingRadius;

            foreach (Vector2 off in Offsets2D)
            {
                uint key = GetKeyFromHash(HashCell((int)(CenterX + off.X), (int)(CenterY + off.Y)));
                int cellStartIndex = StartIndices[key];

                for (int i = cellStartIndex; i < SpatialLookup.Length; i++)
                {
                    if (SpatialLookup[i].CKey != key) break;

                    int particleIndex = SpatialLookup[i].index;
                    float sqrDist = (parts[particleIndex].PredPoint - samplePoint).LengthSquared();
                    if (sqrDist <= sqrRadius)
                    {
                        float dist = Vector2.Distance(samplePoint, parts[particleIndex].PredPoint);

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
        Vector2 CalcPresureForce(int index)
        {
            Vector2 PressureForce = new Vector2(0, 0);

            (int CenterX, int CenterY) = PositionToCellCoord(parts[index].PredPoint, smoothingRadius);
            float sqrRadius = smoothingRadius * smoothingRadius;

            foreach (Vector2 off in Offsets2D)
            {
                uint key = GetKeyFromHash(HashCell((int)(CenterX + off.X), (int)(CenterY + off.Y)));
                int cellStartIndex = StartIndices[key];

                for (int i = cellStartIndex; i < SpatialLookup.Length; i++)
                {
                    if (SpatialLookup[i].CKey != key) break;

                    int particleIndex = SpatialLookup[i].index;
                    float sqrDist = (parts[particleIndex].point - parts[index].point).LengthSquared();
                    if (sqrDist <= sqrRadius)
                    {
                        if (index == particleIndex) continue;
                        Vector2 offset = parts[particleIndex].PredPoint - parts[index].PredPoint;
                        float dist = offset.Length();
                        Vector2 dir = dist == 0 ? GetRandomDir() : offset / dist;

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
        private Vector2 GetRandomDir()
        {
            Random r = new Random();
            return new Vector2(r.Next(0, 1), r.Next(0, 1));
        }
        float ConvertDensityToPressure(float density)
        {
            float densityError = density - targetDensity;
            float pressure = densityError * pressureMultiplier;
            return pressure;
        }
        #endregion

        #region Viscosity
        public Vector2 CalculateViscosityForce(int index)
        {
            Vector2 ViscosityForce = Vector2.Zero;

            (int CenterX, int CenterY) = PositionToCellCoord(parts[index].point, smoothingRadius);
            float sqrRadius = smoothingRadius * smoothingRadius;

            foreach (Vector2 off in Offsets2D)
            {
                uint key = GetKeyFromHash(HashCell((int)(CenterX + off.X), (int)(CenterY + off.Y)));
                int cellStartIndex = StartIndices[key];

                for (int i = cellStartIndex; i < SpatialLookup.Length; i++)
                {
                    if (SpatialLookup[i].CKey != key) break;

                    int particleIndex = SpatialLookup[i].index;
                    float sqrDist = (parts[particleIndex].point - parts[index].point).LengthSquared();
                    if (sqrDist <= sqrRadius)
                    {
                        if (index == particleIndex) continue;

                        float dist = Vector2.Distance(parts[index].point, parts[particleIndex].point);
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
        public void UpdateSpatialLookup(List<Particle2D> parts, float radius)
        {
            Parallel.For(0, parts.Count, i =>
            {
                (int cellX, int cellY) = PositionToCellCoord(parts[i], radius);
                uint CKey = GetKeyFromHash(HashCell(cellX, cellY));
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
        private uint HashCell(int cellX, int cellY)
        {
            uint a = (uint)(cellX * 15823);
            uint b = (uint)(cellY * 9737333);
            return a + b;
        }
        private uint GetKeyFromHash(uint HC)
        {
            return HC % (uint)SpatialLookup.Length;
        }
        private (int cellX, int cellY) PositionToCellCoord(Particle2D particle, float radius)
        {
            int cellX = (int)(particle.PredPoint.X / radius);
            int cellY = (int)(particle.PredPoint.Y / radius);
            return (cellX, cellY);
        }
        private (int cellX, int cellY) PositionToCellCoord(Vector2 Point, float radius)
        {
            int cellX = (int)(Point.X / radius);
            int cellY = (int)(Point.Y / radius);
            return (cellX, cellY);
        }
        #endregion
    }
}