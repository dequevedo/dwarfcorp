
using System;
using System.Runtime.ExceptionServices;
using System.Runtime.Serialization;
using System.Security;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;

namespace DwarfCorp
{
    public class Shader : Effect
    {
        /// <summary>
        /// Maximum number of dynamic lights passed per frame to the terrain / entity
        /// shaders (TexturedShaders.fx). MUST equal the `#define MAX_LIGHTS` in that
        /// shader source — the shader declares `float3 xLightPositions[MAX_LIGHTS]`
        /// and this C# array is copied straight into that effect parameter.
        ///
        /// History: pre-migration this was 64 on the C# side and 16 in the shader;
        /// FNA's MojoShader toolchain silently truncated at the shader boundary so
        /// the mismatch was invisible. MonoGame's DX11 EffectParameter.SetValue
        /// (EffectParameter.cs ~line 380) loops `for(i=0; i&lt;value.Length; i++)`
        /// into Elements[i] and throws IndexOutOfRangeException once i exceeds
        /// the compiled shader's array bounds — which is exactly what crashed
        /// PlayState right after the M.2 migration.
        ///
        /// Current fix: keep both sides at 16. The light-picking loop in
        /// WorldRenderer.FillClosestLights already sorted by distance and only the
        /// closest 16 visibly contributed under FNA, so lowering from 64 is not
        /// a gameplay regression — it just matches what actually rendered.
        ///
        /// Proper long-term redesign (see PERF_PLAN_EXTREME.md Fase E.2′ and
        /// TODO item 27): switch to a clustered / tiled forward lighting pass.
        /// CPU builds a per-screen-tile list of active lights; the shader only
        /// evaluates O(k) lights per pixel where k ≤ tile depth. That removes the
        /// fixed-size array and the need for these two constants to stay in sync.
        /// Until that ships, any change to this value MUST be mirrored in
        /// Content/Shaders/TexturedShaders.fx `#define MAX_LIGHTS` and the
        /// content has to be rebuilt via `dotnet mgcb`.
        /// </summary>
        public const int MaxLights = 16;

        // Cached clone of TexturedShaders reused across GUI icon generators. Cloning the
        // effect repeatedly triggers an AccessViolationException inside MOJOSHADER_cloneEffect
        // on FNA 19.07 (TODO #24). Creating one clone and reusing it avoids the repeat path
        // and lets a single failure short-circuit to a placeholder instead of crashing the game.
        private static Shader _sharedIconShader;
        private static readonly object _sharedIconShaderLock = new object();
        private static bool _sharedIconShaderFailed;

        [HandleProcessCorruptedStateExceptions, SecurityCritical]
        public static Shader TryGetSharedIconShader(Microsoft.Xna.Framework.Content.ContentManager Content, string effectPath)
        {
            lock (_sharedIconShaderLock)
            {
                if (_sharedIconShader != null) return _sharedIconShader;
                if (_sharedIconShaderFailed) return null;

                CrashBreadcrumbs.Push("Shader.TryGetSharedIconShader: cloning " + effectPath);
                try
                {
                    var source = Content.Load<Effect>(effectPath);
                    _sharedIconShader = new Shader(source, true);
                    CrashBreadcrumbs.Push("Shader.TryGetSharedIconShader: clone succeeded");
                    return _sharedIconShader;
                }
                catch (Exception e)
                {
                    _sharedIconShaderFailed = true;
                    CrashBreadcrumbs.Push("Shader.TryGetSharedIconShader: FAILED — " + e.GetType().Name + ": " + e.Message);
                    Console.Error.WriteLine("Shader clone failed (will use placeholder icons): " + e);
                    return null;
                }
            }
        }

        public Vector3[] LightPositions
        {
            set
            {
                // Defensive clamp: if the C# array is ever larger than the shader's
                // compiled xLightPositions array capacity, MonoGame's DX11 backend
                // throws IndexOutOfRangeException inside EffectParameter.SetValue
                // (it iterates Elements[i] up to value.Length). With MaxLights held
                // in lockstep with the shader's #define MAX_LIGHTS (both = 16), the
                // `else` branch below should never execute in steady state —
                // but keeping the guard is cheap and stops a future drift between
                // C# and the .fx file from crashing the render thread. The proper
                // structural fix (clustered forward lighting) is tracked in
                // PERF_PLAN_EXTREME.md Fase E.2′ and TODO_LIST item 27.
                var p = Parameters["xLightPositions"];
                int shaderCapacity = p.Elements.Count;
                if (shaderCapacity == 0 || value.Length <= shaderCapacity)
                {
                    p.SetValue(value);
                }
                else
                {
                    var clamped = new Vector3[shaderCapacity];
                    Array.Copy(value, clamped, shaderCapacity);
                    p.SetValue(clamped);
                }
            }
        }

        /// <summary>
        /// DIAGNOSTIC TOGGLE for post-migration 3D rendering issue: when true,
        /// all matrix parameter setters apply `Matrix.Transpose()` before copying
        /// to the effect. Hypothesis: FNA/MojoShader used row-major convention
        /// transparently; MonoGame DX11 expects column-major in HLSL. If this
        /// toggle flips a black viewport to visible, that's confirmation — and
        /// the permanent fix is to either add `row_major` to the shader matrix
        /// declarations or bake the transpose into the setters (removing the
        /// toggle). Controlled from the Render Inspector ImGui panel (F12).
        /// </summary>
        public static bool TransposeMatrices = false;

        private static void SetMatrix(EffectParameter p, Matrix m)
        {
            if (TransposeMatrices) m = Matrix.Transpose(m);
            p.SetValue(m);
        }

        public Matrix View
        {
            get { return Parameters["xView"].GetValueMatrix(); }
            set { SetMatrix(Parameters["xView"], value); }
        }

        public Matrix Projection
        {
            get { return Parameters["xProjection"].GetValueMatrix(); }
            set { SetMatrix(Parameters["xProjection"], value); }
        }

        public Matrix World
        {
            get { return Parameters["xWorld"].GetValueMatrix(); }
            set { SetMatrix(Parameters["xWorld"], value); }
        }

        public Matrix LightView
        {
            get { return Parameters["xLightView"].GetValueMatrix(); }
            set { SetMatrix(Parameters["xLightView"], value); }
        }

        public Matrix LightProjection
        {
            get { return Parameters["xLightProj"].GetValueMatrix(); }
            set { SetMatrix(Parameters["xLightProj"], value); }
        }

        public Matrix ReflectionView
        {
            get { return Parameters["xReflectionView"].GetValueMatrix(); }
            set { SetMatrix(Parameters["xReflectionView"], value); }
        }

        public float WaterOpacity
        {
            get { return Parameters["xWaterOpacity"].GetValueSingle(); }
            set {  Parameters["xWaterOpacity"].SetValue(value);}
        }

        public float MinWaterOpacity
        {
            get { return Parameters["xWaterMinOpacity"].GetValueSingle(); }
            set { Parameters["xWaterMinOpacity"].SetValue(value);}
        }

        public float CaveView
        {
            get { return Parameters["xCaveView"].GetValueSingle(); }
            set { Parameters["xCaveView"].SetValue(value); }
        }

        public bool EnableLighting
        {
            get { return Parameters["xEnableLighting"].GetValueInt32() > 0; }
            set {  Parameters["xEnableLighting"].SetValue(value ? 1 : 0);}
        }

        public int ActiveLights
        {
            get { return Parameters["ActiveLights"]?.GetValueInt32() ?? 0; }
            set { Parameters["ActiveLights"]?.SetValue(value); }
        }

        public bool EnableShadows
        {
            // Shadows were disabled during the XNA->Mono->FNA migration and the
            // xEnableShadows shader parameter was never ported. Property is a stub.
            get { return false; }
            set { }
        }

        public Texture2D WaterBumpMap
        {
            get { return Parameters["xWaterBumpMap"].GetValueTexture2D(); }
            set {  Parameters["xWaterBumpMap"].SetValue(value);}
        }

        public float WaveLength
        {
            get { return Parameters["xWaveLength"].GetValueSingle(); }
            set { Parameters["xWaveLength"].SetValue(value);}
        }

        public float WaveHeight
        {
            get { return Parameters["xWaveHeight"].GetValueSingle(); }
            set { Parameters["xWaveHeight"].SetValue(value);}
        }

        public Vector3 CameraPosition
        {
            get { return Parameters["xCamPos"].GetValueVector3(); }
            set {  Parameters["xCamPos"].SetValue(value);}
        }

        public float Time
        {
            get { return Parameters["xTime"].GetValueSingle(); }
            set {  Parameters["xTime"].SetValue(value);}
        }

        public float TimeOfDay
        {
            get { return Parameters["xTimeOfDay"].GetValueSingle(); }
            set {  Parameters["xTimeOfDay"].SetValue(value);}
        }

        public float WindForce
        {
            get { return Parameters["xWindForce"].GetValueSingle(); }
            set {  Parameters["xWindForce"].SetValue(value);}
        }

        public Vector3 WindDirection
        {
            get { return Parameters["xWindDirection"].GetValueVector3(); }
            set {  Parameters["xWindDirection"].SetValue(value); }
        }

        public bool EnbleFog
        {
            get { return Parameters["xEnableFog"].GetValueInt32() > 0; }
            set {  Parameters["xEnableFog"].SetValue(value ? 1 : 0);}
        }

        public float FogStart
        {
            get { return Parameters["xFogStart"].GetValueSingle(); }
            set {  Parameters["xFogStart"].SetValue(value);}
        }

        public float FogEnd
        {
            get { return Parameters["xFogEnd"].GetValueSingle(); }
            set {  Parameters["xFogEnd"].SetValue(value);}
        }

        public Color FogColor
        {
            get {  return new Color(Parameters["xFogColor"].GetValueVector3());}
            set {  Parameters["xFogColor"].SetValue(value.ToVector3());}
        }

        public Color RippleColor
        {
            get {  return new Color(Parameters["xRippleColor"].GetValueVector4());}
            set {  Parameters["xRippleColor"].SetValue(value.ToVector4());}
        }

        public Color FlatWaterColor
        {
            get {  return new Color(Parameters["xFlatColor"].GetValueVector4());}
            set {  Parameters["xFlatColor"].SetValue(value.ToVector4());}
        }

        public Vector2 PixelSize
        {
            get { return Parameters["pixelSize"].GetValueVector2(); }
            set {  Parameters["pixelSize"].SetValue(value);}
        }

        public Vector4 SelectionBufferColor
        {
            get { return Parameters["xID"].GetValueVector4(); }
            set {  Parameters["xID"].SetValue(value);}
        }

        public bool ClippingEnabled
        {
            get { return Parameters["Clipping"].GetValueInt32() > 0; }
            set { Parameters["Clipping"].SetValue(value ? 1 : 0);}
        }

        public bool GhostClippingEnabled
        {
            get { return Parameters["GhostMode"].GetValueInt32() > 0; }
            set {  Parameters["GhostMode"].SetValue(value ? 1 : 0);}
        }

        public bool SelfIlluminationEnabled
        {
            get { return Parameters["SelfIllumination"].GetValueInt32() > 0; }
            set {  Parameters["SelfIllumination"].SetValue(value ? 1 : 0);}
        }

        public Vector4 ClipPlane
        {
            get { return Parameters["ClipPlane0"].GetValueVector4(); }
            set { Parameters["ClipPlane0"].SetValue(value); }
        }

        public Texture2D MainTexture
        {
            get { return Parameters["xTexture"].GetValueTexture2D(); }
            set {  Parameters["xTexture"].SetValue(value);
                if (value != null)
                {
                    TextureWidth = value.Width;
                    TextureHeight = value.Height;
                }
            }
        }

        public Texture2D SelfIlluminationTexture
        {
            get { return Parameters["xIllumination"].GetValueTexture2D(); }
            set {  Parameters["xIllumination"].SetValue(value);}
        }


        public Texture2D WaterReflectionMap
        {
            get { return Parameters["xReflectionMap"].GetValueTexture2D(); }
            set {  Parameters["xReflectionMap"].SetValue(value);}
        }

        public float WaterReflectance
        {
            get { return Parameters["xWaterReflective"].GetValueSingle(); }
            set {  Parameters["xWaterReflective"].SetValue(value);}
        }

        public Texture2D SunlightGradient
        {
            get { return Parameters["xSunGradient"].GetValueTexture2D(); }
            set {  Parameters["xSunGradient"].SetValue(value);}
        }

        public Texture2D AmbientOcclusionGradient
        {
            get { return Parameters["xAmbientGradient"].GetValueTexture2D(); }
            set {  Parameters["xAmbientGradient"].SetValue(value);}
        }

        public Texture2D TorchlightGradient
        {
            get { return Parameters["xTorchGradient"].GetValueTexture2D(); }
            set {  Parameters["xTorchGradient"].SetValue(value);}
        }

        public Texture2D WaterShoreGradient
        {
            get { return Parameters["xShoreGradient"].GetValueTexture2D(); }
            set {  Parameters["xShoreGradient"].SetValue(value);}
        }

        public Texture2D LightMap
        {
            get { return Parameters["xLightmap"].GetValueTexture2D(); }
            set {  Parameters["xLightmap"].SetValue(value);}
        }

        public Color LightRamp
        {
            get {  return new Color(Parameters["xLightRamp"].GetValueVector4());}
            set {  Parameters["xLightRamp"].SetValue(value.ToVector4());}
        }

        public Color VertexColorTint
        {
            get {  return new Color(Parameters["xVertexColorMultiplier"].GetValueVector4());}
            set {  Parameters["xVertexColorMultiplier"].SetValue(value.ToVector4());}
        }

        public Texture2D ShadowMap
        {
            get { return Parameters["xShadowMap"].GetValueTexture2D(); }
            set { Parameters["xShadowMap"].SetValue(value);}
        }

        public bool EnableWind
        {
            get { return Parameters["xEnableWind"].GetValueInt32() > 0; }
            set { Parameters["xEnableWind"].SetValue(value ? 1 : 0); }
        }

        public int TextureWidth
        {
            get { return Parameters["xTextureWidth"].GetValueInt32(); }
            set { Parameters["xTextureWidth"].SetValue(value); }
        }

        public int TextureHeight
        {
            get { return Parameters["xTextureHeight"].GetValueInt32(); }
            set { Parameters["xTextureHeight"].SetValue(value); }
        }

        public int ScreenWidth
        {
            get { return Parameters["xScreenWidth"].GetValueInt32(); }
            set { Parameters["xScreenWidth"].SetValue(value); }
        }

        public int ScreenHeight
        {
            get { return Parameters["xScreenHeight"].GetValueInt32(); }
            set { Parameters["xScreenHeight"].SetValue(value); }
        }


        public int CurrentNumLights { get; set; }

        public class Technique
        {
            public static string Icon = "Icon";
            public static string Water = "Water";
            public static string WaterFlat = "WaterFlat";
            public static string WaterTextured = "WaterTextured";
            public static string Untextured = "Untextured";
            public static string Untextured_Pulse = "Untextured_Pulse";
            public static string ShadowMap = "Shadow";
            public static string ShadowMapInstanced = "ShadowInstanced";
            public static string SelectionBuffer = "Selection";
            public static string Textured_ = "Textured";
            public static string TexturedFlag = "Textured_Flag";
            public static string TexturedWithLightmap = "Textured_From_Lightmap";
            public static string Lightmap = "Lightmap";
            public static string TexturedWithColorScale = "Textured_colorscale";
            public static string Instanced_ = "Instanced";
            public static string TiledInstanced_ = "TiledInstanced";
            public static string SelectionBufferInstanced = "Instanced_SelectionBuffer";
            public static string SelectionBufferTiledInstanced = "TiledInstanced_SelectionBuffer";
            public static string Silhouette = "Silhouette";
            public static string Stipple = "Textured_Stipple";
        }

        public void SetTexturedTechnique()
        {
            ActiveLights = CurrentNumLights;
            CurrentTechnique = Techniques["Textured"];
        }

        public void SetIconTechnique()
        {
            CurrentTechnique = Techniques[Shader.Technique.Icon];
        }

        public void SetInstancedTechnique()
        {
            ActiveLights = CurrentNumLights;
            CurrentTechnique = Techniques["Instanced"];
        }

        public void SetTiledInstancedTechnique()
        {
            ActiveLights = CurrentNumLights;
            CurrentTechnique = Techniques["TiledInstanced"];
        }


        public Shader(Effect cloneSource, bool defaults) :
            this(cloneSource)
        {
            if (defaults)
            {
                SetDefaults();
            }
        }

        protected Shader(Effect cloneSource) 
            : base(cloneSource)
        {
        }

        public void SetDefaults()
        {
            FogStart = 40.0f;
            FogEnd = 80.0f;
            CaveView = 0.0f;
            LightView = Matrix.Identity;
            LightProjection = Matrix.Identity;
            EnableWind = false;
        }
    }
}
