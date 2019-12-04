﻿#ifndef MYRP_LIT_INCLUDED
#define MYRP_LIT_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

CBUFFER_START(UnityPerFrame)
    float4x4 unity_MatrixVP;
CBUFFER_END

CBUFFER_START(UnityPerDraw)
    float4x4 unity_ObjectToWorld;
CBUFFER_END

#define UNITY_MATRIX_M unity_ObjectToWorld

#define MAX_VISIBLE_LIGHTS 4

CBUFFER_START(_LightBuffer)
    float4 _VisibleLightColors[MAX_VISIBLE_LIGHTS];
    float4 _VisibleLightDirectionsOrPositions[MAX_VISIBLE_LIGHTS];
    float4 _VisibleLightAttenuations[MAX_VISIBLE_LIGHTS];
    float4 _VisibleSpotLightDirections[MAX_VISIBLE_LIGHTS];
CBUFFER_END

float3 DiffuseLight(int index, float3 normal, float3 worldPos)
{
    float3 lightColor = _VisibleLightColors[index];
    float3 lightVector = _VisibleLightDirectionsOrPositions[index].xyz - worldPos.xyz * _VisibleLightDirectionsOrPositions[index].w;
    float3 lightDirection = normalize(lightVector);
    float ndotl = saturate(dot(normal, lightDirection)); // 

    // range attenuation
    float4 lightAttenuation = _VisibleLightAttenuations[index];
    float fadeRange = dot(lightVector, lightVector) * lightAttenuation.x;
    fadeRange = saturate(1 - fadeRange * fadeRange);
    fadeRange *= fadeRange;

    // distance attenuation
    float distanceSqr = max(dot(lightVector, lightVector), 0.00001);
    float diffuse = ndotl * fadeRange / distanceSqr;

    return lightColor * diffuse;
}

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"

UNITY_INSTANCING_BUFFER_START(PerInstance)
    UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
UNITY_INSTANCING_BUFFER_END(PerInstance)

struct VertexInput
{
    float4 pos : POSITION;
    float3 normal : NORMAL;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct VertexOutput
{
    float4 clipPos : SV_POSITION;
    float3 normal : TEXCOORD0;
    float3 worldPos : TEXCOORD1;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};



VertexOutput LitPassVertex(VertexInput input)
{
    VertexOutput output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    float4 worldPos = mul(UNITY_MATRIX_M, float4(input.pos.xyz, 1));
    output.clipPos = mul(unity_MatrixVP, worldPos);
    output.normal = mul((float3x3)UNITY_MATRIX_M, input.normal);
    output.worldPos = worldPos;
    return output;
}

float4 LitPassFragment (VertexOutput input) : SV_TARGET 
{
    UNITY_SETUP_INSTANCE_ID(input);
    float3 albedo = UNITY_ACCESS_INSTANCED_PROP(PerInstance, _Color);
    input.normal =  normalize(input.normal);
    //float diffuseLight = saturate(dot(input.normal, float3(0, 1, 0)));
    float3 diffuseLight = 0;

    for (int i = 0; i < MAX_VISIBLE_LIGHTS; i++)
    {
        diffuseLight += DiffuseLight(i, input.normal, input.worldPos);
    }

    float3 color = albedo * diffuseLight;
	return float4(color, 1);
}

#endif // MYRP_LIT_INCLUDED
