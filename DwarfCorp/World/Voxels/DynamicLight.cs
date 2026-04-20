using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using DwarfCorp.GameStates;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;

namespace DwarfCorp
{
    public class DynamicLight
    {
        public float Range { get; set; }
        public float Intensity { get; set; }
        public Vector3 Position { get; set; }

        public static List<DynamicLight> Lights = new List<DynamicLight>(); // Todo: Ew!!

        // Fase C.3: pooled transient-light scratch. Particle emitters emit short-lived
        // DynamicLight instances every frame; this used to be `TempLights.Add(new DynamicLight(...))`
        // per emitting particle per frame, which with lit particle emitters added up to
        // tens of KB/sec of Gen 0 pressure. Now `ClearTempLights()` resets a counter,
        // `AddTempLight(...)` reuses slots from the backing list; the list grows to peak
        // usage and then stops allocating. Read via `TempLightCount` / `GetTempLight(i)`.
        private static readonly List<DynamicLight> _tempLightPool = new List<DynamicLight>();
        private static int _tempLightCount;

        public static int TempLightCount => _tempLightCount;
        public static DynamicLight GetTempLight(int index) => _tempLightPool[index];

        public static void ClearTempLights() => _tempLightCount = 0;

        public static void AddTempLight(float range, float intensity, Vector3 position)
        {
            DynamicLight light;
            if (_tempLightCount < _tempLightPool.Count)
            {
                light = _tempLightPool[_tempLightCount];
            }
            else
            {
                light = new DynamicLight(range, intensity, add: false);
                _tempLightPool.Add(light);
            }
            light.Range = range;
            light.Intensity = intensity;
            light.Position = position;
            _tempLightCount++;
        }

        public DynamicLight()
        {
            
        }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            Lights.Add(this);
        }

        public DynamicLight(float range, float intensity, bool add = true)
        {
            Range = range;
            Intensity = intensity;

            if (add)
            {
                Lights.Add(this);
            }
        }

        public void Destroy()
        {
            Lights.Remove(this);
        }
    }
}