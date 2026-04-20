using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using DwarfCorp.GameStates;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Graphics;
using System.Threading;
using Newtonsoft.Json;

namespace DwarfCorp
{
    public static class ComponentRenderer
    {
        public enum WaterRenderType
        {
            Reflective,
            None
        }

        // Fase C.3: signature moved from IEnumerable<GameComponent> to the concrete
        // List<GameComponent> so foreach uses the struct enumerator (zero alloc) and
        // callers can reuse a single scratch list across frames. The previous
        // IEnumerable-based calls forced a boxed enumerator per foreach plus a full
        // LINQ chain re-run every invocation.
        public static void RenderSelectionBuffer(
            List<GameComponent> Renderables,
            DwarfTime time,
            ChunkManager chunks,
            Camera camera,
            SpriteBatch spriteBatch,
            GraphicsDevice graphics,
            Shader effect)
        {
            effect.CurrentTechnique = effect.Techniques["Selection"];
            for (int i = 0; i < Renderables.Count; i++)
                Renderables[i].RenderSelectionBuffer(time, chunks, camera, spriteBatch, graphics, effect);
        }

        public static void Render(
            List<GameComponent> Renderables,
            DwarfTime gameTime,
            ChunkManager chunks,
            Camera Camera,
            SpriteBatch spriteBatch,
            GraphicsDevice graphicsDevice,
            Shader effect,
            WaterRenderType waterRenderMode,
            float waterLevel)
        {
            effect.EnableLighting = GameSettings.Current.CursorLightEnabled;
            graphicsDevice.RasterizerState = RasterizerState.CullNone;

            if (waterRenderMode == WaterRenderType.Reflective)
            {
                for (int i = 0; i < Renderables.Count; i++)
                {
                    var bodyToDraw = Renderables[i];
                    if (!(bodyToDraw.GetBoundingBox().Min.Y > waterLevel - 2))
                        continue;

                    bodyToDraw.Render(gameTime, chunks, Camera, spriteBatch, graphicsDevice, effect, true);
                }
            }
            else
            {
                for (int i = 0; i < Renderables.Count; i++)
                    Renderables[i].Render(gameTime, chunks, Camera, spriteBatch, graphicsDevice, effect, false);
            }

            effect.EnableLighting = false;
        }
    }
}
