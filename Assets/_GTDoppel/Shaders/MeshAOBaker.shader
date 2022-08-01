Shader "GeoTetra/MeshAOBaker"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 4.5
            #pragma shader_feature_local CONTRIBUTING_OBJECT

            #include "UnityCG.cginc"
            #include "Quaternion.hlsl"
            #include "Matrix.hlsl"

            struct appdata
            {
                float3 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 normal : NORMAL;
                float2 rectUL : RECT0;
                float2 rectBR : RECT1;
                float4 scrPos : SCREEN_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            int _RenderTextureSizeX;            
            int _RenderTextureSizeY;
            int _CellSize;
            StructuredBuffer<float4x4> _MatrixBuffer;
            StructuredBuffer<float3> _Vertices;
            StructuredBuffer<float3> _Normals;
            float3 _BakeObject_LossyScale;
            float4x4 _BakeObject_WorldToLocalMatrix;
            float4x4 _BakeCamera_WorldToCameraMatrix;
            float4x4 _BakeCamera_ProjectionMatrix;

            float4x4 _ContributingMatrix;

            inline float4x4 VertexTransformMatrix(int index, bool withScale)
            {
                float3 vertNormal = _Normals[index];
                float3 vertPosition = _Vertices[index];
                float4 vertRot = q_look_at(vertNormal, float3(0,1,0));
                float4 rotation = q_inverse(vertRot);
                float3 position = rotate_point(rotation, -vertPosition);
                if (withScale)
                {
                    float4x4 transformMatrix = mul(scale_to_matrix(_BakeObject_LossyScale), position_to_matrix(position));
                    return mul(transformMatrix, quaternion_to_matrix(rotation));
                }
                return mul(position_to_matrix(position), quaternion_to_matrix(rotation));
            }
            
            inline float4x4 MultiplyCameraMatrix(float4x4 transformMatrix)
            {
                float4x4 compoundMatrix = mul(_BakeCamera_WorldToCameraMatrix, transformMatrix);
                compoundMatrix = mul(_BakeCamera_ProjectionMatrix, compoundMatrix);
                return compoundMatrix;
            }            

            v2f vert (appdata v, uint instanceID : SV_InstanceID)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                
                #ifndef CONTRIBUTING_OBJECT
                    float4x4 bakingVertexViewMatrix = VertexTransformMatrix(instanceID, true);
                    float4x4 viewProjectionMatrix = MultiplyCameraMatrix(bakingVertexViewMatrix);
                #else
                    // scale will get applied via the _BakeObject_WorldToLocalMatrix so don't do it twice!
                    float4x4 bakingVertexViewMatrix = VertexTransformMatrix(instanceID, false);
                    // float4x4 contributingTransformMatrix = unity_ObjectToWorld;
                    float4x4 contributingTransformMatrix = _ContributingMatrix;
                    contributingTransformMatrix = mul(_BakeObject_WorldToLocalMatrix, contributingTransformMatrix);
                    contributingTransformMatrix = mul(bakingVertexViewMatrix, contributingTransformMatrix);
                    float4x4 viewProjectionMatrix = MultiplyCameraMatrix(contributingTransformMatrix);
                #endif
                
                o.pos = mul(viewProjectionMatrix, float4(v.vertex, 1));
                o.scrPos = ComputeNonStereoScreenPos(o.pos);

                int2 resolution = int2(_RenderTextureSizeX, _RenderTextureSizeY);
                int2 cellSize = int2(_CellSize, _CellSize);
                int2 gridCount = resolution / cellSize;
                int2 gridPos = int2(instanceID % gridCount.x, instanceID / gridCount.y);
                float2 scale = 1.0 / gridCount;
                float2 rectStep = 2.0 / gridCount;
                float2 rectHalfStep = 1.0 / gridCount;
                float2 rectCenterStart = -1 + rectHalfStep;
                float2 rectCenter = rectCenterStart + rectStep * gridPos;
                o.rectUL = rectCenter - rectHalfStep;
                o.rectBR = rectCenter + rectHalfStep;
                
                float4x4 translation = {
                    scale.x, 0, 0, rectCenter.x,
                    0, scale.y, 0, rectCenter.y,
                    0, 0, 1, 0,
                    0, 0, 0, 1
                };
                o.pos = mul(translation, o.pos);

                o.normal = v.normal;
                
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                
                const float2 centerUV  = i.scrPos.xy / i.scrPos.w;
                if (centerUV.x < 0 || centerUV.x > 1 || centerUV.y < 0 || centerUV.y > 1)
                {
                    discard;
                }
                
                return fixed4(0,0,0,1);
            }
            ENDHLSL
        }
    }
}
