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
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 4.5

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 rectUL : RECT0;
                float2 rectBR : RECT1;
                float4 scrPos : SCREEN_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            int _RenderTextureSizeX;            
            int _RenderTextureSizeY;
            int _CellSize;
            StructuredBuffer<float4x4> _MatrixBuffer;

            v2f vert (appdata v, uint instanceID : SV_InstanceID)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                
                o.pos = mul(_MatrixBuffer[instanceID], v.vertex);
                // o.pos = mul(o.pos, UNITY_MATRIX_VP);
                // o.pos = UnityObjectToClipPos(distortedPos);
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
                
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);

                const float2 centerUV  = i.scrPos.xy / i.scrPos.w;
                if (centerUV.x < 0 || centerUV.x > 1 || centerUV.y < 0 || centerUV.y > 1)
                {
                    discard;
                }
                
                return fixed4(0,0,0,1);
            }
            ENDCG
        }
    }
}
