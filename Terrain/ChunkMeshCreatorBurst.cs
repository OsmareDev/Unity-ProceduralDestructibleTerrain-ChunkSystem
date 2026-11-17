using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using System;

public class ChunkMeshCreatorBurst : SceneSingleton<ChunkMeshCreatorBurst> {
    // ───────────────────────────────────────────────────────────────────────────────────────────────────────────
    // PROPERTIES ────────────────────────────────────────────────────────────────────────────────────────────────
    [SerializeField] private ComputeShader  m_marchingAlgorithm;
    
    private Dictionary<int2, int>           m_vertexIndexMap;
    private List<Vector3>                   m_processedVertices;
    private List<Vector3>                   m_processedNormals;
    private List<int>                       m_processedTriangles;

    // ───────────────────────────────────────────────────────────────────────────────────────────────────────────
    // CONSTANTS ─────────────────────────────────────────────────────────────────────────────────────────────────
    private const int                       MAX_BUFFER_POOL_SIZE    = 10;

    // ───────────────────────────────────────────────────────────────────────────────────────────────────────────
    // BUFFER POOL ───────────────────────────────────────────────────────────────────────────────────────────────
    private Stack<BufferSet>                m_bufferPool;

    private class BufferSet 
    {
        public ComputeBuffer pointsBuffer;
        public ComputeBuffer triangleBuffer;
    }

    #region UnityLyfecycle
    protected override void Awake() {
        base.Awake();

        m_bufferPool = new Stack<BufferSet>();
        m_vertexIndexMap = new Dictionary<int2, int>();
        m_processedVertices = new List<Vector3>();
        m_processedNormals = new List<Vector3>();
        m_processedTriangles = new List<int>();
    }

    protected void OnDestroy() {
        foreach (var bufferSet in m_bufferPool) {
            ComputeHelpers.TryReleasing(bufferSet.pointsBuffer, bufferSet.triangleBuffer);
        }
        m_bufferPool.Clear();
    }

    #endregion
    #region BuffersetControl
    private BufferSet GetOrCreateBufferSet(int numPoints, int maxTriangles) {
        if (m_bufferPool.Count <= 0) {
            BufferSet newSet = new BufferSet {
                pointsBuffer = new ComputeBuffer(numPoints, sizeof(float)),
                triangleBuffer = new ComputeBuffer(maxTriangles, ComputeHelpers.GetStride<Triangle>(), ComputeBufferType.Append),
            };

            return newSet;
        }

        return m_bufferPool.Pop();
    }

    private void ReturnBufferSet(BufferSet bufferSet) {
        if (m_bufferPool.Count > MAX_BUFFER_POOL_SIZE) {
            ComputeHelpers.TryReleasing(bufferSet.pointsBuffer, bufferSet.triangleBuffer);
        }

        m_bufferPool.Push(bufferSet);
    }

    #endregion
    #region MeshCreation
    public void GenerateMesh(ChunkLiteBurst chunk, bool useFlatShading = false) {
        if (!chunk.HeightMap.IsCreated) {
            Debug.LogError("Chunk heightmap is not created!");
            return;
        }

        int numPoints = chunk.HeightMap.Length;
        int chunkSize = ChunkManagerLiteBurst.Instance.ChunkSize;
        int resolution = ChunkManagerLiteBurst.Instance.ChunkResolution;

        int numVoxelsPerAxis = resolution - 1;
        int numVoxels = numVoxelsPerAxis * numVoxelsPerAxis * numVoxelsPerAxis;
        int maxTriangleCount = numVoxels * 5;

        BufferSet bufferSet = GetOrCreateBufferSet(numPoints, maxTriangleCount);

        try {
            // Upload data to GPU
            bufferSet.pointsBuffer.SetData(chunk.HeightMap);
            bufferSet.triangleBuffer.SetCounterValue(0);

            // Configure compute shader
            m_marchingAlgorithm.SetInt("chunkSize", chunkSize);
            m_marchingAlgorithm.SetInt("resolution", resolution);
            m_marchingAlgorithm.SetBuffer(0, "chunkValues", bufferSet.pointsBuffer);
            m_marchingAlgorithm.SetBuffer(0, "outputTriangles", bufferSet.triangleBuffer);

            ComputeHelpers.Dispatch(
                m_marchingAlgorithm, 
                resolution, 
                resolution, 
                resolution, 
                m_marchingAlgorithm.FindKernel("MarchingCubesLite")
            );

            Triangle[] triangles = ReadTrianglesFromGPU(bufferSet.triangleBuffer);

            ProcessMeshData(chunk, triangles, useFlatShading);
        } finally {
            ReturnBufferSet(bufferSet);
        }
    }

    private Triangle[] ReadTrianglesFromGPU(ComputeBuffer triangleBuffer) {
        ComputeBuffer countBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);
        ComputeBuffer.CopyCount(triangleBuffer, countBuffer, 0);

        int[] triangleCountArray = { 0 };
        countBuffer.GetData(triangleCountArray);
        int triangleCount = triangleCountArray[0];
        ComputeHelpers.TryReleasing(countBuffer);

        if (triangleCount == 0) {
            return Array.Empty<Triangle>();
        }

        Triangle[] triangles = new Triangle[triangleCount];
        triangleBuffer.GetData(triangles, 0, 0, triangleCount);

        return triangles;
    }

    private void ProcessMeshData(ChunkLiteBurst chunk, Triangle[] triangles, bool useFlatShading) {
        m_vertexIndexMap.Clear();
        m_processedVertices.Clear();
        m_processedNormals.Clear();
        m_processedTriangles.Clear();

        int triangleIndex = 0;

        for (int i = 0; i < triangles.Length; i++) {
            Triangle tri = triangles[i];
            VertexData[] vertices = new VertexData[3] { tri.vertexA, tri.vertexB, tri.vertexC };

            for (int j = 0; j < 3; j++) {
                VertexData vert = vertices[j];

                if (!useFlatShading && m_vertexIndexMap.TryGetValue(vert.id, out int sharedIndex)) {
                    // Reuse existing vertex
                    m_processedTriangles.Add(sharedIndex);
                } else {
                    // Add new vertex
                    if (!useFlatShading) {
                        m_vertexIndexMap.Add(vert.id, triangleIndex);
                    }

                    m_processedVertices.Add(vert.position);
                    m_processedNormals.Add(vert.normal);
                    m_processedTriangles.Add(triangleIndex);

                    triangleIndex++;
                }
            }
        }

        ApplyMeshData(chunk, useFlatShading);
    }

    private void ApplyMeshData(ChunkLiteBurst chunk, bool useFlatShading) {
        chunk.MeshCollider.sharedMesh = null;

        chunk.Mesh.Clear();
        chunk.Mesh.SetVertices(m_processedVertices);
        chunk.Mesh.SetTriangles(m_processedTriangles, 0, true);

        if (useFlatShading) {
            chunk.Mesh.RecalculateNormals();
        } else {
            chunk.Mesh.SetNormals(m_processedNormals);
        }

        chunk.Mesh.RecalculateBounds();

        chunk.MeshCollider.sharedMesh = chunk.Mesh;
    }

    public void GenerateMeshBatch(List<ChunkLiteBurst> chunks, bool useFlatShading = false) {
        foreach (var chunk in chunks) {
            GenerateMesh(chunk, useFlatShading);
        }
    }

    #endregion
}
