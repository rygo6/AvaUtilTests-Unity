Shader "Unlit/PseudoAOBaker"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        
        _AngleMin ("Angle Min", Range(-2,2)) = -1
        _AngleMax ("Anlge max", Range(-2,2)) = 1
        
        _DistanceMax ("Distance Min", Range(0,2)) = 1
        
        _Mult ("Multiply", Range(1,2)) = 1.2
        _Bias ("Bias", Range(0,10)) = 6
        
    }
    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
        }

        Pass
        {
            Cull off
            Conservative true

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma exclude_renderers d3d11_9x
            #pragma exclude_renderers d3d9

            #include "UnityCG.cginc"

            struct appdata
            {
                float3 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float3 worldVertex : WORLDPOSITION;
                float ao : AO;
            };

            #define INFINITY_FLOAT 3.402823466e+38F
            
            StructuredBuffer<float3> _vertices;
            StructuredBuffer<float3> _normals;
            int _vertLength;
            float _AngleMin;
            float _AngleMax;
            float _DistanceMax;
            float _Mult;
            float _Bias;
            
            float lengthsq(float3 x) { return dot(x, x); }

            float distancesq(float x, float y) { return (y - x) * (y - x); }
            
            float linearStep(float a, float b, float x)
            {
	            return saturate((x - a)/(b - a));
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = float4(v.uv.xy * 2.0 - 1.0, 0.5, 1.0);
                o.vertex.y = -o.vertex.y;
                // o.vertex = UnityObjectToClipPos(v.vertex);
                o.worldVertex = mul(unity_ObjectToWorld, float4( v.vertex, 1.0 ) ).xyz;
                const float3 wrldNorm = UnityObjectToWorldNormal(v.normal);
                
                float length = _vertLength;
                // float length = 1;
                float divisor = length * 2.0;
                
                float outCol = 0;
                for (int vert = 0; vert < length; ++vert)
                {
                    float3 worldVert = mul(unity_ObjectToWorld, float4( _vertices[vert], 1.0 ) ).xyz;
                    float3 worldVertDirection = normalize(o.worldVertex - worldVert);
                    float worldVertDot = dot(worldVertDirection, wrldNorm);
                    worldVertDot = linearStep(_AngleMin, _AngleMax, worldVertDot);
                    // float3 worldVert = _vertices[v];
                    float dist = distance(o.worldVertex, worldVert);
                    dist = linearStep(0, _DistanceMax, dist);
                    outCol += worldVertDot / divisor;
                    outCol += dist / divisor;
                }

                outCol *= _Mult;
                outCol = pow(outCol, _Bias);
                
                o.ao = outCol;
                
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                // return float4(i.worldVertex, 1);
                return i.ao;
            }
            ENDHLSL
        }
    }
}