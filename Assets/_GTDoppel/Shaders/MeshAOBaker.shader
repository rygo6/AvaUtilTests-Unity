Shader "GeoTetra/MeshAOBaker"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
//        Blend One OneMinusSrcAlpha
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 5.0
            #pragma shader_feature_local CONTRIBUTING_OBJECT

            #include "UnityCG.cginc"
            #include "Quaternion.hlsl"
            #include "Matrix.hlsl"

            struct appdata
            {
                float3 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 clipRect : TEXCOORD0;
                // float3 normal : NORMAL;
                // float4 scrPos : SCREEN_POSITION;
            };

            int _CurrentTestIndex;;            
            int _RenderTextureSizeX;            
            int _RenderTextureSizeY;
            int _CellSize;
            StructuredBuffer<float3> _Vertices;
            StructuredBuffer<float3> _Normals;
            StructuredBuffer<float4> _Tangents;
            float3 _BakeObject_LossyScale;
            float4x4 _BakeObject_WorldToLocalMatrix;
            float4x4 _BakeObject_LocalToWorldMatrix;
            float4x4 _BakeCamera_WorldToCameraMatrix;
            float4x4 _BakeCamera_ProjectionMatrix;
            float4x4 _FinalRotationMatrix;
            float4 _FinalRotation;
            float4x4 _ContributingMatrix;
            float4 _FinalClippings;

            inline float4x4 VertexTransformMatrix(int index)
            {                
                float3 vertNormal = _Normals[index];
                float3 vertPosition = _Vertices[index];
                float3 vertOriginOffset = float3(0, 0, 0) - vertPosition; // essentially -vertPosition but typing it out to remember
                float3 vertTangent = _Tangents[index].xyz * _Tangents[index].w;
                float4 vertRot = q_look_at(vertNormal, vertTangent);
                
                float4 bakeRot = q_inverse(vertRot);
                float4x4 bakeRotMatrix = quaternion_to_matrix(bakeRot);
                
                float3 bakePos = vertOriginOffset;
                float4x4 bakePosMatrix = position_to_matrix(bakePos);
                
                float4x4 scaleMatrix = scale_to_matrix(_BakeObject_LossyScale);
                
                #ifndef CONTRIBUTING_OBJECT
                    float4x4 transformMatrix = bakePosMatrix;
                    transformMatrix = mul(bakeRotMatrix, transformMatrix);
                    transformMatrix = mul(_FinalRotationMatrix, transformMatrix);
                    transformMatrix = mul(scaleMatrix, transformMatrix);
                    return transformMatrix;
                #else // Contributing Object
                    // move to vert
                    float4x4 transformMatrix = bakePosMatrix;
                    // rotate to proper direction
                    transformMatrix = mul(bakeRotMatrix, transformMatrix);
                    transformMatrix = mul(_FinalRotationMatrix, transformMatrix);
                    // don't scale?
                    // transformMatrix = mul(scaleMatrix, transformMatrix);
                    return transformMatrix;
                #endif
            }
            
            inline float4x4 MultiplyCameraMatrix(float4x4 transformMatrix)
            {
                float4x4 compoundMatrix = mul(_BakeCamera_WorldToCameraMatrix, transformMatrix);
                compoundMatrix = mul(_BakeCamera_ProjectionMatrix, compoundMatrix);
                return compoundMatrix;
            }            

            v2f vert (
                appdata v,
                uint instanceID : SV_InstanceID)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);

                // instanceID = _CurrentTestIndex;
                
                #ifndef CONTRIBUTING_OBJECT
                    float4x4 bakingVertexViewMatrix = VertexTransformMatrix(instanceID);
                    float4x4 viewProjectionMatrix = MultiplyCameraMatrix(bakingVertexViewMatrix);
                #else // Contributing Object
                    float4x4 bakingVertexViewMatrix = VertexTransformMatrix(instanceID);
                    float4x4 contributingTransformMatrix = _ContributingMatrix;

                    // transform contributing matrix to local space of baking matrix
                    contributingTransformMatrix = mul(_BakeObject_WorldToLocalMatrix, contributingTransformMatrix);

                    // apply bakingVertexViewMatrix within that local space of baking matrix
                    contributingTransformMatrix = mul(bakingVertexViewMatrix, contributingTransformMatrix);

                    // apply camera projection
                    float4x4 viewProjectionMatrix = MultiplyCameraMatrix(contributingTransformMatrix);
                #endif
                
                float4 pos = mul(viewProjectionMatrix, float4(v.vertex, 1));

                int2 resolution = int2(_RenderTextureSizeX, _RenderTextureSizeY);
                int2 cellSize = int2(_CellSize, _CellSize);
                int2 gridCount = resolution / cellSize;
                int2 gridPos = int2(instanceID % gridCount.x, instanceID / gridCount.y);
                float2 scale = 1.0 / gridCount;
                float2 rectStep = 2.0 / gridCount;
                float2 rectHalfStep = 1.0 / gridCount;
                float2 rectCenterStart = -1 + rectHalfStep;
                float2 rectCenter = rectCenterStart + rectStep * gridPos;
                
                float4x4 translation = {
                    scale.x, 0, 0, rectCenter.x,
                    0, scale.y, 0, rectCenter.y,
                    0, 0, 1, 0,
                    0, 0, 0, 1
                };

                float2 rectFragStep = 1.0 / gridCount;
                float2 rectFragHalfStep = rectFragStep / 2.0;
                float2 rectFragCenterStart = rectFragHalfStep + rectFragStep * gridPos;
                o.clipRect = float4(rectFragCenterStart.x - rectFragHalfStep.x, rectFragCenterStart.x + rectFragHalfStep.x,
                                    rectFragCenterStart.y - rectFragHalfStep.y, rectFragCenterStart.y + rectFragHalfStep.y);

                o.pos = mul(translation, pos);

                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float2 centerUV = i.pos.xy / float2(_RenderTextureSizeX, _RenderTextureSizeY);
                centerUV.y = 1 - centerUV.y;
                if (centerUV.x < i.clipRect.x || centerUV.x > i.clipRect.y ||
                    centerUV.y < i.clipRect.z || centerUV.y > i.clipRect.w)
                {
                    discard;
                }

                // if (centerUV.x < _FinalClippings.x || centerUV.x > _FinalClippings.y || centerUV.y < _FinalClippings.z || centerUV.y > _FinalClippings.w)
                // {
                //     discard;
                // }
                
                // return float4(i.pos.w, i.pos.w, i.pos.w, 1.0);
                return float4(0,0,0,1);
            }
            ENDHLSL
        }
    }
}
