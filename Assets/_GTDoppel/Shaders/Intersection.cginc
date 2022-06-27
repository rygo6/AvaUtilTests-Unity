StructuredBuffer<float3> verts;
StructuredBuffer<int> indices;
int numVerts;
int numIndices;

float _AngleMin;
float _AngleMax;
float _DistanceMax;

#define FLT_EPSILON 1.192092896e-07
#define INFINITY_FLOAT 1 - FLT_EPSILON

// from https://www.graphicon.ru/html/2012/conference/EN2%20-%20Graphics/gc2012Shumskiy.pdf
float intersect(float3 orig, float3 dir, float3 v0, float3 v1, float3 v2) 
{ 
    float3 e1 = v1 - v0;
    float3 e2 = v2 - v0;
    float3 normal = normalize(cross(e1, e2));
    float b = dot(normal, dir);
    float3 w0 = orig - v0;
    float a = -dot(normal, w0);
    float t = a / b;
    float3 p = orig + t * dir;
    float uu, uv, vv, wu, wv, inverseD;
    uu = dot(e1, e1);
    uv = dot(e1, e2);
    vv = dot(e2, e2);
    float3 w = p - v0;
    wu = dot(w, e1);
    wv = dot(w, e2);
    inverseD = uv * uv - uu * vv;
    inverseD = 1.0f / inverseD;
    float u = (uv * wv - vv * wu) * inverseD;
    if (u < 0.0f || u > 1.0f)
        return -1.0f;
    float v = (uv * wu - uu * wv) * inverseD;
    if (v < 0.0f || (u + v) > 1.0f)
        return -1.0f;
    // UV = float2(u,v);
    return t;
}

float bruteTrace(float3 rayOrigin, float3 rayDirection)
{
    float currentT = INFINITY_FLOAT;
    
    for (int i = 0; i < numIndices; i += 3)
    {
        int ia = indices[i];
        int ib = indices[i + 1];
        int ic = indices[i + 2];
        
        float3 a = verts[ia];
        float3 b = verts[ib];
        float3 c = verts[ic];

        float3 wA = mul(unity_ObjectToWorld, float4( a, 1.0 ) ).xyz;
        float3 wB = mul(unity_ObjectToWorld, float4( b, 1.0 ) ).xyz;
        float3 wC = mul(unity_ObjectToWorld, float4( c, 1.0 ) ).xyz;
        
        float newT = intersect(rayOrigin + (rayDirection * FLT_EPSILON), rayDirection, wA, wB, wC);
        if (newT > 0 && newT < currentT) 
        {
            currentT = newT;
        }
    }
    
    return currentT;
}

float linearStep(float a, float b, float x)
{
    return saturate((x - a)/(b - a));
}

void pseudoTriAO(float3 origin, float3 direction, inout float outCol)
{
    float divisor = numVerts * 2.0;
    for (int i = 0; i < numIndices; i += 3)
    {
        int ia = indices[i];
        int ib = indices[i + 1];
        int ic = indices[i + 2];
        
        float3 a = verts[ia];
        float3 b = verts[ib];
        float3 c = verts[ic];

        float3 average = (a + b + c) / 3.0;
        float3 worldVert = mul(unity_ObjectToWorld, float4( average, 1.0 ) ).xyz;
        float3 worldVertDirection = normalize(worldVert - origin);
        float worldVertDot = dot(worldVertDirection, direction);
        
        // if (worldVertDot > _AngleMax)
        // {
        //     outCol += worldVertDot / divisor;
        // }

        float dist = distance(origin, worldVert);
        if (dist < _DistanceMax)
            outCol += dist / divisor;
    }
}

void pseudoAO(float3 origin, float3 direction, inout float outCol)
{
    int count = numVerts;
    float divisor = count * 2.0;
    for (int vert = 0; vert < count; ++vert)
    {                    
        float3 worldVert = mul(unity_ObjectToWorld, float4( verts[vert], 1.0 ) ).xyz;
        float3 worldVertDirection = normalize(worldVert - origin);
        float worldVertDot = dot(worldVertDirection, direction);

        // if (worldVertDot > 0)
        // {
        //     outCol += worldVertDot / divisor;
        // }
        
        float dist = distance(origin, worldVert);
        if (dist < _DistanceMax)
            outCol += dist / divisor;
    }
}