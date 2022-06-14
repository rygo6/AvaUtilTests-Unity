Shader "Unlit/SplineBakerShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
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

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma exclude_renderers d3d11_9x
            #pragma exclude_renderers d3d9

            #include "UnityCG.cginc"
            #include "Packages/com.unity.splines/Shader/Spline.cginc"

            struct appdata
            {
                float3 vertex : POSITION0;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : POSITION0;
                float3 worldVertex : POSITION1;
            };

            #define INFINITY_FLOAT 3.402823466e+38F
            #define RESOLUTIONS_SEGMENT_COUNT_MIN 4
            #define RESOLUTION_SEGMENT_COUNT_MAX 1024
            #define SPLINE_ITERATIONS 2
            #define SPLINE_RESOLUTION 64

            SplineInfo info;
            StructuredBuffer<BezierCurve> curves;
            StructuredBuffer<float> curveLengths;

            struct Segment
            {
                float start, length;
            };

            // have to copy from cginc and make it reference uniform curveLengths buffer otherwise cant compile with UNITY_LOOP?
            float SplineToCurveTInline(const SplineInfo info, const float splineT)
            {
                const uint knotCount = GetKnotCount(info);
                const bool closed = GetSplineClosed(info);
                const float targetLength = saturate(splineT) * GetSplineLength(info);
                float start = 0;

                UNITY_LOOP
                for (int i = 0, c = closed ? knotCount : knotCount - 1; i < c; ++i)
                {
                    const float curveLength = curveLengths[i];

                    if (targetLength <= (start + curveLength))
                    {
                        // knot index unit stores curve index in integer part, and curve t in fractional. that means it cannot accurately
                        // represent the absolute end of a spline. so instead we check for it and return a value that's really close.
                        // if we don't check, this method would happily return knotCount+1, which is fine for closed loops but not open.
                        return i + clamp((targetLength - start) / curveLength, 0, .9999);
                    }

                    start += curveLength;
                }

                return closed ? 0 : knotCount-2 + .9999;
            }
            
            float3 GetPosition(float splineT)
            {
                float curve = SplineToCurveTInline(info, splineT);
                return EvaluatePosition(curves[floor(curve) % GetKnotCount(info)], frac(curve));
            }

            float lengthsq(float3 x) { return dot(x, x); }

            float distancesq(float x, float y) { return (y - x) * (y - x); }

            float3 PointLineSegmentNearestPoint(float3 p, float3 a, float3 b, out float t)
            {
                float l2 = lengthsq(b - a);

                if (l2 == 0.0)
                {
                    t = 0.0;
                    return a;
                }

                t = dot(p - a, b - a) / l2;

                if (t < 0.0)
                    return a;
                if (t > 1.0)
                    return b;

                return a + t * (b - a);
            }

            Segment GetNearestPoint(
                float3 startPoint,
                Segment range,
                out float distance,
                int segments)
            {
                distance = INFINITY_FLOAT;
                Segment segment;
                segment.start = -1.0;
                segment.length = 0.0;

                float t0 = range.start;
                float3 a = GetPosition(t0);
                float dsqr = INFINITY_FLOAT;
                
                UNITY_LOOP
                for (int i = 1; i < segments; i++)
                {
                    float t1 = range.start + (range.length * (i / (segments - 1.0)));
                    float3 b = GetPosition(t1);
                    float st;
                    float3 p = PointLineSegmentNearestPoint(startPoint, a, b, st);
                    float d = lengthsq(p - startPoint);

                    if (d < dsqr)
                    {
                        segment.start = t0;
                        segment.length = t1 - t0;
                        dsqr = d;
                    }

                    t0 = t1;
                    a = b;
                }

                distance = sqrt(dsqr);
                
                return segment;
            }

            int GetSegmentCount(float length, int resolution)
            {
                return (int)max(RESOLUTIONS_SEGMENT_COUNT_MIN, min(RESOLUTION_SEGMENT_COUNT_MAX, sqrt(length) * resolution));
            }

            float GetNearestPoint(float3 startPoint)
            {
                float distance = INFINITY_FLOAT;
                Segment segment;
                segment.start = 0.0;
                segment.length = 1.0;
                
                for (int i = 0; i < SPLINE_ITERATIONS; i++)
                {
                    int segments = GetSegmentCount(GetSplineLength(info) * segment.length, SPLINE_RESOLUTION);
                    segment = GetNearestPoint(startPoint, segment, distance, segments);
                }

                return distance;
            }

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
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float distance = GetNearestPoint(i.worldVertex);
                // float outCol = pow(1 - distance, 32);
                float outCol = linearStep(.95, 1, 1 - distance);
                // float outCol = smoothstep(.9, 1, 1 - distance);
                return float4(outCol.xxx, 1);
                // return float4(curves[3].P0, 1);
                
                // float distMult = 1 - distance;
                // // distance *= 100;
                // // distance = pow(distance, 100);
                //
                // return float4(distance, distance, distance, 1);
                // // return float4(t, t, t, 1);
                // // return float4(nearest, 1);
                
                // return float4(i.worldVertex, 1.0);
                // return i.vertex;
                // return float4(1,0,1, 1.0);
            }
            ENDHLSL
        }
    }
}