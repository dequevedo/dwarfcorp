using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace DwarfCorp
{
    public class PhysicsSystem : EngineModule
    {
        [UpdateSystemFactory]
        private static EngineModule __factory(WorldManager World)
        {
            return new PhysicsSystem();
        }

        public override ModuleManager.UpdateTypes UpdatesWanted => ModuleManager.UpdateTypes.Update;
        
        public override void Update(DwarfTime GameTime, WorldManager World)
        {
            var physicsObject = 0;
            PerformanceMonitor.PushFrame("PhysicsSystem");
            // Fase C.3: was `foreach (var p in ComponentUpdateSet.OfType<Physics>())`,
            // which allocated the OfType<> iterator state machine and boxed the HashSet
            // enumerator every frame. Plain foreach + type check is zero-alloc.
            foreach (var component in World.ComponentUpdateSet)
            {
                if (component is Physics physicsComponent)
                {
                    physicsObject += 1;
                    physicsComponent.PhysicsUpdate(GameTime, World.ChunkManager);
                }
            }
            PerformanceMonitor.SetMetric("Physics Objects", physicsObject);
            PerformanceMonitor.PopFrame();
        }
    }
}
