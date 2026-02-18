//UNITY_SHADER_NO_UPGRADE
#ifndef SHAPE_RENDER_INCLUDED
#define SHAPE_RENDER_INCLUDED

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

void RenderSingleEye(float2 uv, float2 eyePos, float eyeRadius, inout float3 color, inout float alpha) {
    eyeRadius *= (1.0 + sin(_Time[1]) * 0.25);
    float eyeDist = CircleSDF(uv, eyePos, eyeRadius);
    float eyeAlpha = SDFtoAlpha(eyeDist, 0.0025);
    
    float2 highlightPos = float2(1,1) * (eyeRadius * 0.5);
    float highlightRadius = (eyeRadius * 0.2) * (1.0 + sin(_Time[3] * 4) * 0.05);
    float highlightDist = CircleSDF(uv, eyePos + highlightPos, highlightRadius);
    float highlightAlpha = SDFtoAlpha(highlightDist, 0.0025);

    color = lerp(color, float3(0,0,0), eyeAlpha);
    color = lerp(color, float3(1,1,1), highlightAlpha);
    alpha = max(alpha, eyeAlpha);
    alpha = max(alpha, highlightAlpha);
}

void RenderMouth(float2 uv, float2 mouthPos, inout float3 color, inout float alpha) {
    // Todo: transform uv and/or mouthPos into coords relative to the function

    uv -= mouthPos;
    uv *= float2(8 + 4 * sin(_Time[1]), 8 + 2 * sin(_Time[2]));

    float mouthDist = sdRoundedBox(uv, mouthPos, float4(0.2, 0.4, 0.2, 0.4));
    float mouthAlpha = SDFtoAlpha(mouthDist, 0.0025 * 8);

    color = lerp(color, float3(0,0,0), mouthAlpha);
    alpha = max(alpha, mouthAlpha);
}

void RenderFace_float(float2 uv, float2 facePos, float2 eyeRadius, out float3 color, out float alpha) {
    color = float3(1,1,1);
    alpha = 0;

    RenderSingleEye(uv, facePos + float2(-0.2, +0.1), eyeRadius, color, alpha);
    RenderSingleEye(uv, facePos + float2(+0.2, +0.1), eyeRadius, color, alpha);
    RenderMouth(uv, facePos + float2(0, -0.1), color, alpha);
    
    // color = float3(1,0,0);
    // alpha = 1;
}

#endif