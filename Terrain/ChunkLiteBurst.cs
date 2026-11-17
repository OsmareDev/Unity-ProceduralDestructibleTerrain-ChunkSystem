using Unity.Collections;
using UnityEngine;
using System;

public class ChunkLiteBurst : IPoolable, IEquatable<ChunkLiteBurst> {
    // ── PROPERTIES ────────────────────────────────────────────────────────────────────────────────────────────
    private Vector2Int          m_coords; 
    public Vector2Int           Coords          { get => m_coords; set => SetCoordinates(value); }

    private NativeArray<float>  m_heightMap;
    public NativeArray<float>   HeightMap       => m_heightMap;

    // ── FLAGS ─────────────────────────────────────────────────────────────────────────────────────────────────
    private bool                m_isLoaded;
    public bool                 IsLoaded        { get => m_isLoaded; set => m_isLoaded = value; }

    private bool                m_hasMesh;
    public bool                 HasMesh         { get => m_hasMesh; set => m_hasMesh = value; }

    // ── REFERENCES ────────────────────────────────────────────────────────────────────────────────────────────
    private GameObject          m_chunkGO;
    public GameObject           GameObject      { get => m_chunkGO; }

    private Mesh                m_mesh;
    public Mesh                 Mesh            { get => m_mesh; }

    private MeshCollider        m_meshCollider;
    public MeshCollider         MeshCollider    { get => m_meshCollider; }

    #region Lyfecycle
    public ChunkLiteBurst(Vector2Int coords, int paddedSize) {
        m_heightMap = new NativeArray<float>(paddedSize * paddedSize, Allocator.Persistent);
        m_coords = coords;

        float t = (float)ChunkManagerLiteBurst.Instance.ChunkSize 
                / (float)ChunkManagerLiteBurst.Instance.ChunkResolution;

        m_mesh = new Mesh {
            indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
        };

        m_chunkGO = new GameObject($"Chunk:X{coords.x}_Y{coords.y}");
        m_chunkGO.transform.SetParent(ChunkManagerLiteBurst.Instance.transform);
        m_chunkGO.transform.localPosition = new(
            coords.x * (ChunkManagerLiteBurst.Instance.ChunkSize - t), 0,
            coords.y * (ChunkManagerLiteBurst.Instance.ChunkSize - t)
        );

        m_chunkGO.AddComponent<MeshFilter>().mesh = m_mesh;
        m_chunkGO.AddComponent<MeshRenderer>().material = ChunkManagerLiteBurst.Instance.ChunkMaterial;
        m_meshCollider = m_chunkGO.AddComponent<MeshCollider>();
        
        m_chunkGO.SetActive(false);
    }

    ~ChunkLiteBurst() {
        Dispose();
    }

    #endregion
    #region Helpers
    private void SetCoordinates(Vector2Int newCoords) {
        m_coords = newCoords;

        float t = (float)ChunkManagerLiteBurst.Instance.ChunkSize / (float)ChunkManagerLiteBurst.Instance.ChunkResolution;
        m_chunkGO.transform.localPosition = new(
            newCoords.x * (ChunkManagerLiteBurst.Instance.ChunkSize - t), 0,
            newCoords.y * (ChunkManagerLiteBurst.Instance.ChunkSize - t)
        );
        m_chunkGO.name = $"Chunk:X{newCoords.x}_Y{newCoords.y}";
    }

    #endregion
    #region IPoolable
    public void OnActivate() {
        m_chunkGO.SetActive(true);
    }

    public void OnDeactivate() {
        m_isLoaded = false;
        m_hasMesh = false;
        m_chunkGO.SetActive(false);
        
        // Clear mesh data
        m_mesh.Clear();
        m_meshCollider.sharedMesh = null;
    }

    public void Dispose() {
        if (m_heightMap.IsCreated) m_heightMap.Dispose();
        if (m_mesh != null) UnityEngine.Object.Destroy(m_mesh);
        if (m_chunkGO != null) UnityEngine.Object.Destroy(m_chunkGO);
    }

    #endregion
    #region IEquatable
    public override bool Equals(object obj) => Equals(obj as ChunkLiteBurst);
    public bool Equals(ChunkLiteBurst other) {
        if (other is null) return false;
        return Coords == other.Coords;
    }

    public override int GetHashCode() {
        return Coords.GetHashCode();
    }

    #endregion
}