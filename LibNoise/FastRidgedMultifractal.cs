using System;

namespace LibNoise
{
    // Drop-in replacement for the original FastRidgedMultifractal implementation,
    // delegating to FastNoiseLite with FractalType.Ridged. Same public API as
    // before so call sites don't need to change.
    public class FastRidgedMultifractal : IModule
    {
        private FastNoiseLite _noise;
        private double _frequency;
        private double _lacunarity;
        private int _octaveCount;
        private NoiseQuality _noiseQuality;
        private int _seed;

        private const int MaxOctaves = 30;

        public FastRidgedMultifractal() : this(0) { }

        public FastRidgedMultifractal(int seed)
        {
            _frequency = 1.0;
            _lacunarity = 2.0;
            _octaveCount = 6;
            _noiseQuality = NoiseQuality.Standard;
            _seed = seed;
            Rebuild();
        }

        private void Rebuild()
        {
            _noise = new FastNoiseLite(_seed);
            _noise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
            _noise.SetFractalType(FastNoiseLite.FractalType.Ridged);
            _noise.SetFrequency((float)_frequency);
            _noise.SetFractalOctaves(_octaveCount);
            _noise.SetFractalLacunarity((float)_lacunarity);
            _noise.SetFractalGain(0.5f);
        }

        public double Frequency
        {
            get => _frequency;
            set { _frequency = value; _noise.SetFrequency((float)value); }
        }

        public double Lacunarity
        {
            get => _lacunarity;
            set { _lacunarity = value; _noise.SetFractalLacunarity((float)value); }
        }

        public NoiseQuality NoiseQuality
        {
            get => _noiseQuality;
            set => _noiseQuality = value;
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
