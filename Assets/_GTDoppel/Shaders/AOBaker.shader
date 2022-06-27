Shader "Unlit/AOBaker"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        
        _AngleMax ("Anlge max", Range(0,1)) = .5
        _AngleYRotation ("Anlge Y Rotation", Range(0,1)) = .5
        
        _DistanceMax ("Distance Max", Range(0,1)) = 1
        
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
            Cull back
            Conservative true

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma exclude_renderers d3d11_9x
            #pragma exclude_renderers d3d9

            #include "UnityCG.cginc"
            #include "Intersection.cginc"

            struct appdata
            {
                float3 vertex : POSITION;
                float3 normal : NORMAL;
                float3 tangent : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 worldNormal : NORMAL;
                float3 worldTangent : TANGENT;
                float3 worldVertex : WORLDPOSITION;
                float ao : AO;
            };

            RWStructuredBuffer<float> aoVerts;
            float _AngleYRotation;
            float _Mult;
            float _Bias;
            
            float lengthsq(float3 x) { return dot(x, x); }

            float distancesq(float x, float y) { return (y - x) * (y - x); }

            v2f vert(appdata v, uint vid : SV_VertexID)
            {
                v2f o;
                o.vertex = float4(v.uv.xy * 2.0 - 1.0, 0.5, 1.0);
                o.vertex.y = -o.vertex.y;
                // o.vertex = UnityObjectToClipPos(v.vertex);
                
                const float3 worldVertex = mul(unity_ObjectToWorld, float4( v.vertex, 1.0 ) ).xyz;
                const float3 worldNormal = UnityObjectToWorldNormal(v.normal);
                const float3 worldTangent = UnityObjectToWorldNormal(v.tangent);
                o.worldVertex = worldVertex;
                o.worldNormal = worldNormal;
                o.worldTangent = worldTangent;
                
                float3 right = worldTangent;
                float3 up = cross(worldNormal, worldTangent);
                float3 left = cross(worldNormal, up);
                float3 down = cross(worldNormal, left);
                
                float3 sideDirections[4] = {
                    lerp(right, up, _AngleYRotation),
                    lerp(up, left, _AngleYRotation),
                    lerp(left, down, _AngleYRotation),
                    lerp(down, right, _AngleYRotation)
                };

                float rayCount = 5;
                
                float dist = bruteTrace(worldVertex,  worldNormal) / rayCount;
                for (int d = 0; d < 4; ++d)
                {
                    float3 direction = lerp(worldNormal, sideDirections[d], _AngleMax);
                    dist += bruteTrace(worldVertex,  direction) / rayCount;
                }
                dist *= _Mult;
                dist = pow(dist, _Bias);                
                o.ao = dist;
                
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                return i.ao;
            }
            ENDHLSL
        }
    }
}