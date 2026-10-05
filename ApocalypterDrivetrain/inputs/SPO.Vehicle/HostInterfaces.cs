// SPO.Vehicle — the engine-agnostic vehicle model's two host interfaces (SPEC §2, D2). The vehicle
// model (A1+) talks only to these; the Unity/PhysX host (Track A, in game) and the Bepu host
// (harness, Track B) implement them. Skeleton only in M0: no vehicle code yet.
// Constraints (PROMPT 1/5): no UnityEngine, no System.Numerics (Unity 2020.3's Mono profile has no
// System.Numerics.Vectors in Managed/), no libm transcendentals, allocation-free per tick.
// MIT-0 — see /LICENSE.
namespace SPO.Vehicle
{
    /// <summary>The rigid body the vehicle model drives. World space, SI units (m, kg, s, N).</summary>
    public interface IVehicleBody
    {
        Vec3 Position { get; }
        Quat Orientation { get; }
        Vec3 LinearVelocity { get; }
        Vec3 AngularVelocity { get; }
        float Mass { get; }
        /// <summary>Velocity of a world-space point fixed to the body.</summary>
        Vec3 PointVelocity(Vec3 worldPoint);
        /// <summary>Applies a force (N) at a world-space point for the current step.</summary>
        void AddForceAtPoint(Vec3 force, Vec3 worldPoint);
    }

    /// <summary>Result of a suspension cast.</summary>
    public struct WheelHit
    {
        public bool Hit;
        public float Distance;      // along the cast direction, m
        public Vec3 Point, Normal;
        public int SurfaceId;       // host-defined surface/material id
        public long HitBodyId;      // -1 = static world
        public Vec3 HitBodyPointVelocity;
    }

    /// <summary>Casts down a wheel's suspension axis. Hosts: Physics.Raycast/SphereCast (PhysX) or Simulation.RayCast (Bepu).</summary>
    public interface IWheelQuery
    {
        bool Cast(Vec3 origin, Vec3 direction, float maxDistance, float wheelRadius, out WheelHit hit);
    }

    /// <summary>Plain float vector (dependency-free on netstandard2.0 and Unity's Mono).</summary>
    public struct Vec3
    {
        public float X, Y, Z;
        public Vec3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator *(Vec3 a, float s) => new Vec3(a.X * s, a.Y * s, a.Z * s);
        public static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    }

    public struct Quat
    {
        public float X, Y, Z, W;
        public Quat(float x, float y, float z, float w) { X = x; Y = y; Z = z; W = w; }
        public static readonly Quat Identity = new Quat(0, 0, 0, 1);
        /// <summary>Rotates v by this (unit) quaternion.</summary>
        public Vec3 Rotate(Vec3 v)
        {
            var u = new Vec3(X, Y, Z);
            var t = Vec3.Cross(u, v) * 2f;
            return v + t * W + Vec3.Cross(u, t);
        }
    }
}
