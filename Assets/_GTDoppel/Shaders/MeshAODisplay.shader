Shader "GeoTetra/MeshAODisplay"
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
            #pragma target 5.0
            #pragma vertex vert
            #pragma fragment frag
            // #pragma multi_compile_instancing
            
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0; 
                // UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 ao : AO;
                float2 uv : TEXCOORD0; 
                // UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // sampler2D _MainTex;
            uniform Texture2D _MainTex;
            uniform SamplerState _point_clamp_Sampler;
            int _RenderTextureSizeX;            
            int _RenderTextureSizeY;
            int _CellSize;           

            v2f vert (appdata v, uint vertexID: SV_VertexID)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;

                int2 resolution = int2(_RenderTextureSizeX, _RenderTextureSizeY);
                int2 cellSize = int2(_CellSize, _CellSize);
                int2 gridCount = resolution / cellSize;
                int2 gridPos = int2(vertexID % gridCount.x, vertexID / gridCount.y);
                float2 scale = 1.0 / gridCount;
                float2 rectStep = 2.0 / gridCount;
                float2 rectHalfStep = 1.0 / gridCount;
                float2 rectCenterStart = -1 + rectHalfStep;
                float2 rectCenter = rectCenterStart + rectStep * gridPos;
                rectCenter = rectCenter * .5 + .5;
                rectCenter.y = 1 - rectCenter.y;

                float4 sample = _MainTex.SampleLevel(_point_clamp_Sampler, rectCenter, 0);
                // float4 sample = tex2Dlod(_MainTex, float4(rectCenter.xy,0,0));
                o.ao = sample;
                
                // float2 rectUL = rectCenter - rectHalfStep;
                // float2 rectBR = rectCenter + rectHalfStep;

                
                
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);

                // float4 sample = _MainTex.SampleLevel(_point_clamp_Sampler, i.uv, 0);
                // return sample;
                
                // return fixed4(i.ao,i.ao,i.ao,1);
                return i.ao;
            }
            ENDHLSL
        }
    }
}
