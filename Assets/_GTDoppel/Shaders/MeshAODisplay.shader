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
            
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0; 
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 ao : AO;
                float2 uv : TEXCOORD0; 
            };
            
            uniform Texture2D _MainTex;
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
                gridPos.y = gridCount.y - gridPos.y - 1;
                
                float4 sample = _MainTex.Load(int3(gridPos, 0));
                o.ao = sample;
                
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                return i.ao;
            }
            ENDHLSL
        }
    }
}
