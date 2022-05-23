using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
using Random = UnityEngine.Random;

public class VertexFlowBaker : MonoBehaviour
{
    [SerializeField] 
    SkinnedMeshRenderer m_Renderer;

    [SerializeField] 
    MeshFilter m_DisplayMeshFilter;
    
    [SerializeField] 
    Transform m_Armature;

    [SerializeField] 
    Transform[] m_StartPoints;

    const float VertexSearchDistance = 0.0000001f;

    Matrix4x4 m_TransformMatrix;

    Mesh m_Mesh;
    NativeArray<Vector3> m_Vertices;
    NativeArray<Vector3> m_Normals;
    NativeArray<Vector3> m_AveragedFlowDirections;
    // NativeArray<NativeList<Vector3>> m_FlowBakerVertices;
    NativeMultiHashMap<int, Vector3> m_FlowDirections;
    // List<Vector3>[] m_FlowDirections;
    Color[] m_FlowColors;
    List<SubMesh> m_SubMesh = new();
    HashSet<int> m_WalkedVertices = new();
    // Dictionary<int, List<int>> m_OverlappingVerts = new();
    Dictionary<int, HashSet<int>> m_Overlapping = new();
    Dictionary<int, HashSet<int>> m_FlowedFrom = new();
    public class SubMesh
    {
        public SubMeshDescriptor Descriptor;
        public int[] Triangles;
    }

    [Serializable]
    public struct FlowBakerVertex
    {
        public Unity.Mathematics.float3 FlowColor;
        public NativeArray<Vector3> FlowDirections;
        public NativeHashSet<int> OverlappingVertIndices;
        public NativeHashSet<int> FlowedFromVetIndices;

    }
    
    void Start()
    {
        m_TransformMatrix = m_Renderer.localToWorldMatrix;
        
        m_Mesh = new Mesh();
        m_Renderer.BakeMesh(m_Mesh);
        m_DisplayMeshFilter.sharedMesh = m_Mesh;

        m_Vertices = new NativeArray<Vector3>(m_Mesh.vertices, Allocator.Persistent); 
        m_Normals = new NativeArray<Vector3>(m_Mesh.normals, Allocator.Persistent);  
        m_AveragedFlowDirections = new NativeArray<Vector3>(m_Vertices.Length, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        // m_FlowBakerVertices = new NativeArray<NativeList<Vector3>>(m_Vertices.Length, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        // m_FlowDirections = new List<Vector3>[m_Vertices.Length];
        m_FlowDirections = new NativeMultiHashMap<int, int>();
        m_FlowColors = new Color[m_Vertices.Length];
        for (int i = 0; i < m_Vertices.Length; ++i)
        {
            m_FlowedFrom.Add(i, new HashSet<int>());
        }

        for (int i = 0; i < m_Mesh.subMeshCount; ++i)
        {
            var descriptor = m_Mesh.GetSubMesh(i);
            int[] triangles = m_Mesh.GetTriangles(i);
            m_SubMesh.Add(new SubMesh{Descriptor = descriptor, Triangles = triangles});
            Debug.Log($"{descriptor.topology} {descriptor.indexCount} {descriptor.vertexCount}");
        }
        
        StartCoroutine(WalkVertices());
    }

    int GetNearestVertex(Vector3 fromPoint)
    {
        int nearestIndex = -1;
        float nearestDistance = float.MaxValue;
        for (int i = 0; i < m_Vertices.Length; ++i)
        {
            var point = m_TransformMatrix.MultiplyPoint(m_Vertices[i]);
            var distance = (point - fromPoint).sqrMagnitude;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestIndex = i;
            }
        }

        return nearestIndex;
    }

    int m_SearchTasksCompleted = 0;
    IEnumerator CalcOverlappingVerts()
    {
        for (int i = 0; i < m_Vertices.Length; ++i)
        {
            int vertIndex = i;
            m_Overlapping.Add(vertIndex, new HashSet<int>());
            Task.Run(() =>
            {
                for (int searchIndex = 0; searchIndex < m_Vertices.Length; ++searchIndex)
                {
                    if (searchIndex == vertIndex)
                        continue;

                    if ((m_Vertices[vertIndex] - m_Vertices[searchIndex]).sqrMagnitude < VertexSearchDistance)
                    {
                        m_Overlapping[vertIndex].Add(searchIndex);
                    }
                }

                Interlocked.Increment(ref m_SearchTasksCompleted);
            });
        }

        yield return new WaitUntil(() => m_SearchTasksCompleted == m_Vertices.Length);
        Debug.Log("CalcOverlappingVerts Complete");
    }

    Queue<int> m_VertQueue = new();
    IEnumerator WalkVertices()
    {
        yield return CalcOverlappingVerts();
        
        for (int i = 0; i < m_StartPoints.Length; ++i)
        {
            int nearestIndex = GetNearestVertex(m_StartPoints[i].transform.position);
            m_VertQueue.Enqueue(nearestIndex);
            AddWalkedVerticesWithOverlaps(nearestIndex);
        }
        
        List<int> foundTriIndices = new();
        NativeList<Vector3> flowDirections = new();
        int stepCount = 0;
        int subMeshIndex = -1;
        
        while (m_VertQueue.Count  > 0)
        {
            // while (!Input.GetKeyDown(KeyCode.A))
            // {
            //     Debug.DrawRay(  m_TransformMatrix.MultiplyPoint(m_Vertices[m_VertQueue.Peek()]) , m_TransformMatrix.MultiplyVector(m_Normals[m_VertQueue.Peek()]) * .0002f, Color.magenta);
            //     yield return null;
            // }
            
            int vertIndex = m_VertQueue.Dequeue();
            var vertOverlapping = m_Overlapping[vertIndex];
            Vector3 vertPosition = m_Vertices[vertIndex];

            foundTriIndices.Clear();

            if (subMeshIndex == -1)
                SearchAllSubMeshesForVert(vertIndex, foundTriIndices,  ref subMeshIndex);
            else
                SearchSubMeshForVert(vertIndex, foundTriIndices,  subMeshIndex);
            
            // Debug.Log($"Found tries {foundTriIndices.Count} {subMeshIndex}");
            for (int foundTriIndex = 0; foundTriIndex < foundTriIndices.Count; ++foundTriIndex)
            {
                int triIndex = foundTriIndices[foundTriIndex];
                int triIndexStartOffset = triIndex % 3;
                int triIndexStart = triIndex - triIndexStartOffset;

                for (int nextTriIndex = 0; nextTriIndex < 3; ++nextTriIndex)
                {
                    int neighborTriIndex = triIndexStart + nextTriIndex;
                    int neighborVertIndex = m_SubMesh[subMeshIndex].Triangles[neighborTriIndex];
                    if (vertIndex != neighborVertIndex && !vertOverlapping.Contains(neighborVertIndex))
                    {
                        if (AddWalkedVerticesWithOverlaps(neighborVertIndex))
                        {
                            m_VertQueue.Enqueue(neighborVertIndex);
                        }
                        
                        if (!FlowFromAndOverlapsContain(vertIndex, neighborVertIndex) && m_FlowedFrom[neighborVertIndex].Add(vertIndex))
                        {
                            Vector3 neighborVertPosition = m_Vertices[neighborVertIndex];
                            Vector3 flowDirection = neighborVertPosition - vertPosition;
                            Vector3 normalizedFlowDirection = flowDirection.normalized;
                            // m_FlowDirections.ContainsKey()
                            // if (m_FlowDirections[neighborVertIndex] == null)
                            //     m_FlowDirections[neighborVertIndex] = new List<Vector3>();
                            
                            m_FlowDirections.Add(neighborVertIndex, normalizedFlowDirection);
                            // m_FlowDirections[neighborVertIndex].Add(normalizedFlowDirection);
                        }
                    }
                }
            }
            
            stepCount++;
            if (stepCount == 20)
            {
                stepCount = 0;            
                yield return null;
            }
        }
        
        foreach (int walkedVertex in m_WalkedVertices)
        {
            flowDirections.Clear();
            // if (m_FlowDirections[walkedVertex] != null)
            foreach (var VARIABLE in m_FlowDirections[walkedVertex])
            {
                
            }
            flowDirections.AddRange(m_FlowDirections.GetKeyArray());
                    
            if (m_Overlapping.ContainsKey(walkedVertex))
            {
                foreach( var i in m_Overlapping[walkedVertex])
                {
                    if (m_FlowDirections[i] != null)
                        flowDirections.AddRange(m_FlowDirections[i]);
                }
            }

            if (flowDirections.Count > 0)
            {
                Vector3 flowDirection = AverageVector3s(flowDirections);
                m_AveragedFlowDirections[walkedVertex] = flowDirection;
                flowDirection = flowDirection * 0.5f + (Vector3.one * 0.5f);
                Color flowColor = new Color(flowDirection.x, flowDirection.y, flowDirection.z, 1);
                m_FlowColors[walkedVertex] = flowColor;
                if (m_Overlapping.ContainsKey(walkedVertex))
                {
                    foreach( var i in m_Overlapping[walkedVertex])
                    {
                        m_AveragedFlowDirections[i] = m_AveragedFlowDirections[walkedVertex];
                        m_FlowColors[i] = m_FlowColors[walkedVertex];
                    }
                }
            }
        }
        
        m_Mesh.colors = m_FlowColors;
    }

    bool FlowFromAndOverlapsContain(int vertIndex, int neighborIndex)
    {
        var flowedFrom = m_FlowedFrom[vertIndex];
        if (flowedFrom.Contains(neighborIndex))
        {
            return true;
        }
        
        var neighborflowedFrom = m_FlowedFrom[neighborIndex];
        m_RecyclableHasIntersection.Clear();
        m_RecyclableHasIntersection.UnionWith(neighborflowedFrom);
        m_RecyclableHasIntersection.IntersectWith(flowedFrom);
        if (m_RecyclableHasIntersection.Count > 0)
        {
            return true;
        }

        var neighborOverlapping = m_Overlapping[neighborIndex];
        foreach (var neighborOverlappingIndex in neighborOverlapping)
        {
            if (flowedFrom.Contains(neighborOverlappingIndex))
            {
                return true;
            }
            
            var neighborOverlappingFlowedFrom = m_FlowedFrom[neighborOverlappingIndex];
            m_RecyclableHasIntersection.Clear();
            m_RecyclableHasIntersection.UnionWith(neighborOverlappingFlowedFrom);
            m_RecyclableHasIntersection.IntersectWith(flowedFrom);
            if (m_RecyclableHasIntersection.Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    HashSet<int> m_RecyclableHasIntersection = new();

    Vector3 AverageVector3s(List<Vector3> list)
    {
        Vector3 average = Vector3.zero;
        for (int i = 0; i < list.Count; ++i)
        {
            average += list[i];
        }

        average /= list.Count;
        return average;
    }

    bool AddWalkedVerticesWithOverlaps(int index)
    {
        if (m_WalkedVertices.Add(index))
        {
            foreach (int i in m_Overlapping[index])
                m_WalkedVertices.Add(i);

            return true;
        }
        return false;
    }
    
    bool SearchSubMeshForVert(int vert, List<int> triIndices, int subMeshIndex)
    {
        bool foundVert = false;
        var overlappingVerts = m_Overlapping[vert];
        for (int triIndex = 0; triIndex < m_SubMesh[subMeshIndex].Triangles.Length; ++triIndex)
        {
            int triVertIndex = m_SubMesh[subMeshIndex].Triangles[triIndex];
            if (triVertIndex == vert || overlappingVerts.Contains(triVertIndex))
            {
                triIndices.Add(triIndex);
                    foundVert = true;
            }
        }

        return foundVert;
    }

    void SearchAllSubMeshesForVert(int vert, List<int> triIndices, ref int subMeshIndex)
    {
        for (int sm = 0; sm < m_SubMesh.Count; ++sm)
        {
            if (SearchSubMeshForVert(vert, triIndices, sm))
            {
                subMeshIndex = sm;
            }
        }
    }

    Color[] m_RandomColors;
    void OnDrawGizmos()
    {
        if (!Application.isPlaying)       
            return;

        if (m_RandomColors == null)
        {
            m_RandomColors = new Color[m_Vertices.Length];
            for (int i = 0; i < m_RandomColors.Length; ++i)
            {
                Color randomColor = new Color(Random.Range(0f, 1f), Random.Range(0f, 1f), Random.Range(0f, 1f), 1);
                m_RandomColors[i] = randomColor;
            }
        }

        foreach (int walkedVertex in m_WalkedVertices)
        {
            var vertPos = m_TransformMatrix.MultiplyPoint(m_Vertices[walkedVertex]);
            // Gizmos.color = m_RandomColors[walkedVertex];
            // Gizmos.DrawSphere(  vertPos , .0002f);
            // if (m_FlowDirections[walkedVertex] != null)
            // {
            //     for (int i = 0; i < m_FlowDirections[walkedVertex].Count; ++i)
            //     {
            //         Debug.DrawRay(vertPos, m_TransformMatrix.MultiplyVector(m_FlowDirections[walkedVertex][i]) * .001f, new Color(i * 0.1f, i * 0.2f, i * 0.3f, 1));
            //     }
            // }

            foreach (var fromIndex in m_FlowedFrom[walkedVertex])
            {
                var fromPos = m_TransformMatrix.MultiplyPoint(m_Vertices[fromIndex]);
                Vector3 ray = (fromPos - vertPos).normalized; 
                Color color = new Color(ray.x, ray.y, ray.z, 1);
                // Debug.DrawRay(vertPos, ray / 2f, m_RandomColors[walkedVertex]);
                Debug.DrawRay(vertPos, ray * .003f, color);
            }

            // Debug.DrawRay(vertPos, m_TransformMatrix.MultiplyVector(m_AveragedFlowDirections[walkedVertex].normalized) * .005f, Color.yellow);
            // Debug.DrawRay(  m_TransformMatrix.MultiplyPoint(m_Vertices[walkedVertex]) , m_TransformMatrix.MultiplyVector(m_Normals[walkedVertex]) * .0001f, Color.green);
        }

        // for (int i = 0; i < m_VertQueue.Count; ++i)
        // {
        //     int vertIndex = m_VertQueue.ToArray()[i];
        //     Gizmos.color = Color.cyan;
        //     var point = m_TransformMatrix.MultiplyPoint(m_Vertices[vertIndex]);
        //     Gizmos.DrawWireCube(  point , Vector3.one * .0002f);
        //     point.y -= .0002f;
        //
        //     Handles.Label(point, vertIndex.ToString());
        //     foreach (var overlapping in m_Overlapping[vertIndex])
        //     {
        //         point.y -= .0002f;
        //         Handles.Label(point, overlapping.ToString());
        //     }
        // }
        
        
        // Debug.DrawRay(  m_TransformMatrix.MultiplyPoint(m_Vertices[m_NearestIndex]) , m_TransformMatrix.MultiplyVector(m_Normals[m_NearestIndex]) * .001f, Color.red);

        // for (int i = 0; i < m_Vertices.Count; ++i)
        // {
        //     Debug.DrawRay(  m_TransformMatrix.MultiplyPoint(m_Vertices[i]) , m_TransformMatrix.MultiplyVector(m_Normals[i]) * .001f, Color.blue);
        // }
    }
}
