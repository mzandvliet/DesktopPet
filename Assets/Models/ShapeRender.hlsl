//UNITY_SHADER_NO_UPGRADE
#ifndef SHAPE_RENDER_INCLUDED
#define SHAPE_RENDER_INCLUDED

float4 BlendOver(float4 a, float4 b) {
    // premultiply alpha
    a.rgb = a.rgb * a.a;
    b.rgb = b.rgb * b.a;
    
    // blend
    float4 c = b + (1 - b.a) * a;
    
    // unpremultiply
    c.rgb = c.rgb / c.a;

    return c;
}

// https://iquilezles.org/articles/distfunctions2d/

float CircleSDF(float2 uv, float2 center, float radius)
{
    return length(uv - center) - radius;
}

// All coordinates centered at the origin
// b.x = half width
// b.y = half height
// r.x = roundness top-right  
// r.y = roundness boottom-right
// r.z = roundness top-left
// r.w = roundness bottom-left
float sdRoundedBox( in float2 p, in float2 b, in float4 r )
{
    r.xy = (p.x>0.0)?r.xy : r.zw;
    r.x  = (p.y>0.0)?r.x  : r.y;
    float2 q = abs(p)-b+r.x;
    return min(max(q.x,q.y),0.0) + length(max(q,0.0)) - r.x;
}

float SDFtoAlpha(float dist, float aaWidth)
{
    // aaWidth controls softness of edge - fwidth-based is ideal but
    // a small constant like 0.005 works fine for fixed-res eye UVs
    return 1.0 - smoothstep(-aaWidth, aaWidth, dist);
}

void RenderHighlight(float2 uv, float2 pos, float radius, inout float4 color) {
    float dist = CircleSDF(uv, pos, radius);
    float alpha = SDFtoAlpha(dist, 0.0025);
    float4 highlightColor = float4(float3(1,1,1), alpha);
    color = BlendOver(color, highlightColor);
}


void RenderSingleEye(float2 uv, float2 eyePos, float eyeRadius, inout float4 color) {
    eyeRadius *= (1.0 + sin(_Time[3] * 11.37) * 0.025);

    float blinkPhase = _Time[1] % 3.0;
    const float twopi = 6.283185;
    const float blinkDur = 0.2;
    float blinkLerp = saturate(blinkPhase / blinkDur);
    eyeRadius *= 0.1 + 0.9 * cos(blinkLerp * twopi);

    float eyeDist = CircleSDF(uv, eyePos, eyeRadius);
    float eyeAlpha = SDFtoAlpha(eyeDist, 0.0025);
    float4 eyeColor = float4(float3(0,0,0), eyeAlpha);
    color = BlendOver(color, eyeColor);
    
    float2 highlightPos = eyePos + float2(1,1) * (eyeRadius * 0.5);
    float highlightRadius = (eyeRadius * 0.2) * (1.0 + sin(_Time[3] * 4) * 0.05);
    RenderHighlight(uv, highlightPos, highlightRadius, color);
    highlightPos = eyePos + float2(-1,-1) * (eyeRadius * 0.5);
    highlightRadius *= 0.5;
    RenderHighlight(uv, highlightPos, highlightRadius, color);
}

void RenderMouth(float2 uv, float2 mouthPos, inout float4 color) {
    /*
    Todo: transform uv and/or mouthPos into coords relative to the function

    Like, do it properly, understand the space, don't just guess
    */

    uv -= mouthPos;
    float talkSpeed = 2;
    uv *= float2(8 + 2 * sin(_Time[3] * 1.37 * talkSpeed), 16 + 4 * sin(_Time[3] * 1.735 * talkSpeed));

    float mouthDist = sdRoundedBox(uv, mouthPos, float4(0.2, 0.4, 0.2, 0.4));
    float mouthAlpha = SDFtoAlpha(mouthDist, 0.0025 * 8);

    float4 mouthColor = float4(float3(0,0,0) * mouthAlpha, mouthAlpha);
    color = BlendOver(color, mouthColor);
}

void RenderFace_float(float2 uv, float2 facePos, float2 eyeRadius, in float4 inColor, out float4 outColor) {
    outColor = inColor;

    RenderSingleEye(uv, facePos + float2(-0.2, +0.2), eyeRadius, outColor);
    RenderSingleEye(uv, facePos + float2(+0.2, +0.2), eyeRadius, outColor);
    RenderMouth(uv, facePos + float2(0, 0.05), outColor);
}

#endif