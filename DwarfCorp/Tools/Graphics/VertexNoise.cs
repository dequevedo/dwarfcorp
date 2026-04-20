// VertexNoise.cs
//
//  Modified MIT License (MIT)
//
//  Copyright (c) 2015 Completely Fair Games Ltd.
//
// (License preserved verbatim at file history; abbreviated here for readability.)
using Microsoft.Xna.Framework;

namespace DwarfCorp
{
    /// <summary>
    /// Historically this class produced a per-position Y-distortion used to wobble
    /// every voxel-face vertex, every billboarded sprite, and every debug-line a
    /// little bit for an "organic" wavy-terrain look. That distortion was retired
    /// so the codebase could simplify and so that merged quads (Fase B.2 greedy
    /// meshing) wouldn't need to emulate the per-vertex wobble to line up with
    /// neighbours.
    ///
    /// The one surviving API is <see cref="GetRandomNoiseVector"/>, kept because
    /// grass mote placement (<c>VoxelChunk-Motes.cs</c>) uses it to scatter mote
    /// sprites pseudo-randomly on top of a voxel. That's a genuinely random-sample
    /// use case, not a per-vertex distortion, and belongs here for now.
    /// </summary>
    internal static class VertexNoise
    {
        private static readonly Perlin VertexNoiseX = new Perlin(MathFunctions.Random.Next());
        private static readonly Perlin VertexNoiseY = new Perlin(MathFunctions.Random.Next());
        private static readonly Perlin VertexNoiseZ = new Perlin(MathFunctions.Random.Next());
        private static readonly Perlin GlobalVertexNoiseX = new Perlin(MathFunctions.Random.Next());
        private static readonly Perlin GlobalVertexNoiseY = new Perlin(MathFunctions.Random.Next());
        private static readonly Perlin GlobalVertexNoiseZ = new Perlin(MathFunctions.Random.Next());
        private const float NoiseScale = 0.1f;
        private const float NoiseMagnitude = 0.35f;
        private const float GlobalNoiseScale = 0.01f;
        private const float GlobalNoiseMagnitude = 0.1f;

        public static Vector3 GetRandomNoiseVector(float x, float y, float z)
        {
            return GetRandomNoiseVector(new Vector3(x, y, z));
        }

        public static Vector3 GetRandomNoiseVector(Vector3 position)
        {
            float x = VertexNoiseX.Noise(position.X * NoiseScale, position.Y * NoiseScale, position.Z * NoiseScale) * NoiseMagnitude - NoiseMagnitude / 2.0f;
            float y = VertexNoiseY.Noise(position.X * NoiseScale, position.Y * NoiseScale, position.Z * NoiseScale) * NoiseMagnitude - NoiseMagnitude / 2.0f;
            float z = VertexNoiseZ.Noise(position.X * NoiseScale, position.Y * NoiseScale, position.Z * NoiseScale) * NoiseMagnitude - NoiseMagnitude / 2.0f;

            float gx = GlobalVertexNoiseX.Noise(position.X * GlobalNoiseScale, position.Y * GlobalNoiseScale, position.Z * GlobalNoiseScale) * GlobalNoiseMagnitude - GlobalNoiseMagnitude / 2.0f;
            float gy = GlobalVertexNoiseY.Noise(position.X * GlobalNoiseScale, position.Y * GlobalNoiseScale, position.Z * GlobalNoiseScale) * GlobalNoiseMagnitude - GlobalNoiseMagnitude / 2.0f;
            float gz = GlobalVertexNoiseZ.Noise(position.X * GlobalNoiseScale, position.Y * GlobalNoiseScale, position.Z * GlobalNoiseScale) * GlobalNoiseMagnitude - GlobalNoiseMagnitude / 2.0f;

            return new Vector3(x + gx, y + gy, z + gz);
        }
    }
}
