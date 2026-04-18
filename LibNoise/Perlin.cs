using System;

namespace LibNoise
{
    // Drop-in replacement for the original hand-rolled Perlin implementation,
    // delegating to FastNoiseLite (MIT, Auburn/FastNoiseLite). Keeps the same
    // public API (Frequency, Persistence, Lacunarity, Seed, OctaveCount,
    // GetValue) so the 50+ call sites elsewhere in DwarfCorp don't need to
    // change. FastNoiseLite's inner loops are SIMD-friendly and tighter,
    // so this is a straight perf win for chunk gen + mesh noise.
    public class Perlin : IModule
    {
        private FastNoiseLite _noise;
        private double _frequency;
        private double _lacunarity;
        private double _persistence;
        private int _octaveCount;
        private NoiseQuality _noiseQuality;
        private int _seed;

        private const int MaxOctaves = 30;

        public Perlin() : this(0) { }

        public Perlin(int seed)
        {
            _frequency = 1.0;
            _lacunarity = 2.0;
            _octaveCount = 6;
            _persistence = 0.5;
            _noiseQuality = NoiseQuality.Standard;
            _seed = seed;
            Rebuild();
        }

        private void Rebuild()
        {
            _noise = new FastNoiseLite(_seed);
            _noise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
            _noise.SetFractalType(FastNoiseLite.FractalType.FBm);
            _noise.SetFrequency((float)_frequency);
            _noise.SetFractalOctaves(_octaveCount);
            _noise.SetFractalLacunarity((float)_lacunarity);
            _noise.SetFractalGain((float)_persistence);
        }

        public double Frequency
        {
            get => _frequency;
            set { _frequency = value; _noise.SetFrequency((float)value); }
        }

        public double Persistence
        {
            get => _persistence;
            set { _persistence = value; _noise.SetFractalGain((float)value); }
        }

        public double Lacunarity
        {
            get => _lacunarity;
            set { _lacunarity = value; _noise.SetFractalLacunarity((float)value); }
        }

        public NoiseQuality NoiseQuality
        {
            get => _noiseQuality;
            set => _noiseQuality = value; // FastNoiseLite Perlin has a fixed gradient quality — parameter kept for API compat.
        }

        public int Seed
        {
            get => _seed;
            set { _seed = value; _noise.SetSeed(value); }
        }

        public int OctaveCount
        {
            get => _octaveCount;
            set
            {
                if (value < 1 || value > MaxOctaves)
                    throw new ArgumentException("Octave count must be greater than zero and less than " + MaxOctaves);
                _octaveCount = value;
                _noise.SetFractalOctaves(value);
            }
        }

        public double GetValue(double x, double y, double z)
        {
            return _noise.GetNoise((float)x, (float)y, (float)z);
        }
    }
}
