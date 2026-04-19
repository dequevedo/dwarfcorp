// Screen-space outline post-effect — cheap Sobel edge detection on luminance, tinted
// and blended over the original color. Works on color only (no depth/normal buffer
// required), so it's compatible with MojoShader/GLSL120 + any GPU driver.

float4x4 World;
float4x4 View;
float4x4 Projection;

texture TheTexture : register(t0);
sampler TheSampler : register(s0) = sampler_state
{
    Texture = <TheTexture>;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = None;
    AddressU = Clamp;
    AddressV = Clamp;
};

// Per-frame uniforms (bound from OutlineEffect.cs):
//   InverseViewportSize = (1/W, 1/H)
//   OutlineTint         = RGB tint of outline (e.g. 0,0,0 for classic black)
//   OutlineStrength     = 0..1, how strongly outline darkens/tints the pixel
//   EdgeThreshold       = minimum gradient magnitude to consider an edge (0.05-0.25)
//   OutlineThickness    = multiplier on sample offsets (1.0 = 1px, 2.0 = 2px…)
float2 InverseViewportSize;
float3 OutlineTint;
float  OutlineStrength;
float  EdgeThreshold;
float  OutlineThickness;

struct VertexShaderInput
{
    float4 Position : POSITION0;
    float2 TexCoord : TEXCOORD0;
};

struct VertexShaderOutput
{
    float4 Position : POSITION0;
    float2 TexCoord : TEXCOORD0;
};

VertexShaderOutput VertexShaderFunction(VertexShaderInput input)
{
    VertexShaderOutput output;

    float4 worldPosition = mul(input.Position, World);
    float4 viewPosition  = mul(worldPosition, View);
    output.Position      = mul(viewPosition, Projection);
    output.TexCoord      = input.TexCoord;

    return output;
}

float Luma(float3 rgb)
{
    // Rec.709 luma — same weights as the fxaa helper uses for green-as-luma is cheaper,
    // but full luma produces nicer edges on color-based scenes.
    return dot(rgb, float3(0.2126, 0.7152, 0.0722));
}

float4 PixelShaderFunction_Outline(in float2 texCoords : TEXCOORD0) : COLOR0
{
    float4 center = tex2D(TheSampler, texCoords);
    float2 off = InverseViewportSize * OutlineThickness;

    // 3x3 Sobel on luma.
    float tl = Luma(tex2D(TheSampler, texCoords + float2(-off.x, -off.y)).rgb);
    float t  = Luma(tex2D(TheSampler, texCoords + float2(    0, -off.y)).rgb);
    float tr = Luma(tex2D(TheSampler, texCoords + float2( off.x, -off.y)).rgb);
    float l  = Luma(tex2D(TheSampler, texCoords + float2(-off.x,     0)).rgb);
    float r  = Luma(tex2D(TheSampler, texCoords + float2( off.x,     0)).rgb);
    float bl = Luma(tex2D(TheSampler, texCoords + float2(-off.x,  off.y)).rgb);
    float b  = Luma(tex2D(TheSampler, texCoords + float2(    0,  off.y)).rgb);
    float br = Luma(tex2D(TheSampler, texCoords + float2( off.x,  off.y)).rgb);

    float gx = -tl - 2*l - bl + tr + 2*r + br;
    float gy = -tl - 2*t - tr + bl + 2*b + br;
    float edge = sqrt(gx*gx + gy*gy);

    // Smooth threshold so edges fade in rather than popping.
    float amount = saturate((edge - EdgeThreshold) * 4.0) * OutlineStrength;

    float3 tinted = lerp(center.rgb, OutlineTint, amount);
    return float4(tinted, center.a);
}

float4 PixelShaderFunction_Passthrough(in float2 texCoords : TEXCOORD0) : COLOR0
{
    return tex2D(TheSampler, texCoords);
}

technique Outline
{
    pass Pass1
    {
        VertexShader = compile vs_4_0_level_9_3 VertexShaderFunction();
        PixelShader  = compile ps_4_0_level_9_3 PixelShaderFunction_Outline();
    }
}

technique Passthrough
{
    pass Pass1
    {
        VertexShader = compile vs_4_0_level_9_3 VertexShaderFunction();
        PixelShader  = compile ps_4_0_level_9_3 PixelShaderFunction_Passthrough();
    }
}
