using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using Unity.Jobs;

public class ChunkManagerLiteBurst : SceneSingleton<ChunkManagerLiteBurst> {
    [Header("Chunk Settings")] // ───────────────────────────────────────────────────────
    [SerializeField] private int        m_chunkSize             = 16;
    public int                          ChunkSize               => m_chunkSize;

    [SerializeField] private int        m_chunkResolution       = 32;
    public int                          ChunkResolution         => m_chunkResolution;
    public int                          PaddedSize              => m_chunkResolution + 2;

    [Header("Material")] // ─────────────────────────────────────────────────────────────
    [SerializeField] private Material   m_chunkMaterial;
    public Material                     ChunkMaterial           => m_chunkMaterial;

    [Header("Performance Settings")] // ─────────────────────────────────────────────────
    [SerializeField] private int        m_maxChunksPerFrame     = 4;
    [SerializeField] private float      m_unloadDistance        = 100f;

    [Header("Noise Settings")] // ───────────────────────────────────────────────────────
    [SerializeField] private float      m_noiseScale            = 0.01f;
    [SerializeField] private int        m_seed                  = 12345;

    [Header("Batch Settings")] // ───────────────────────────────────────────────────────
    [SerializeField] private int        m_batchFrameInterval    = 4;

    // ──────────────────────────────────────────────────────────────────────────────────
    // INTERNAL STRUCTS ─────────────────────────────────────────────────────────────────
    private struct ModifyRequest 
    {
        public  float2          worldPos;
        public  float           radius;
        public  float           strength;
    }
    private struct ChunkGenerationRequest 
    {
        public  ChunkLiteBurst  chunk;
        public  JobHandle       jobHandle;
    }
    
    // ──────────────────────────────────────────────────────────────────────────────────
    // RUNTIME STATE ────────────────────────────────────────────────────────────────────
    private Dictionary<Vector2Int, ChunkLiteBurst>      m_activeChunks      = new();
    private Dictionary<Vector2Int, ChunkLiteBurst>      m_loadingChunks     = new();
    private GenericPool<ChunkLiteBurst>                 m_chunkPool;

    // Pending Petition Queues (deduplication)
    private HashSet<ChunkLiteBurst>                     m_pendingLoads      = new();
    private HashSet<ChunkLiteBurst>                     m_pendingUnloads    = new();
    private Dictionary<Vector2Int, List<ModifyRequest>> m_pendingModifies   = new();

    // Internal Generation Queue (jobs)
    private Queue<ChunkGenerationRequest>               m_generationQueue   = new();
    private List<JobHandle>                             m_activeJobHandles  = new();

    private int                                         m_frameCounter      = 0;

    #region UnityLifecycle
    protected override void Awake() {
        base.Awake();

        m_chunkPool = new GenericPool<ChunkLiteBurst>(
            factory: () => new ChunkLiteBurst(Vector2Int.zero, PaddedSize)
        );

        if (m_chunkMaterial == null) m_chunkMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
    }

    protected void Start() {
        for (int x = 0; x < 3; x++) for (int y = 0; y < 3; y++) {
            RequestChunkLoad(new Vector2Int(x, y));
        }
    }

    protected void Update() {
        m_frameCounter++;
        if (m_frameCounter >= m_batchFrameInterval) {
            m_frameCounter = 0;
            ProcessBatch();
        }

        ProcessGenerationQueue();
    }

    protected void LateUpdate() {
        for (int i = m_activeJobHandles.Count - 1; i >= 0; i--) {
            if (m_activeJobHandles[i].IsCompleted) {
                m_activeJobHandles[i].Complete();
                m_activeJobHandles.RemoveAt(i);
            }
        }
    }

    protected void OnDestroy() {
        foreach (var handle in m_activeJobHandles) handle.Complete();
        m_activeJobHandles.Clear();

        foreach (var chunk in m_activeChunks.Values) chunk.Dispose();
        m_activeChunks.Clear();

        m_chunkPool.Clear();
    }

    #endregion
    #region PublicRequests
    public void RequestChunkLoad(Vector2Int coords) {
        if (m_activeChunks.ContainsKey(coords) || m_loadingChunks.ContainsKey(coords) ) return;
        
        ChunkLiteBurst chunk = m_chunkPool.Get();
        chunk.Coords = coords;

        m_pendingLoads.Add(chunk);
        m_loadingChunks.Add(coords, chunk);
    }

    public void RequestChunkUnload(Vector2Int coords) {
        if (!m_activeChunks.TryGetValue(coords, out ChunkLiteBurst chunk)) return;
        m_pendingUnloads.Add(chunk);
    }

    public void RequestTerrainModify(Vector3 worldPos, float radius, float strength) {
        Vector2Int center = WorldToChunk(worldPos);
    }

    public void UnloadDistantChunks () {

    }

    public void UnloadOutsideRadius(Vector2Int center, int radius) {
        int r2 = radius * radius;
        foreach (var coord in m_activeChunks.Keys) {
            Vector2Int d = coord - center;
            if (d.x*d.x + d.y*d.y > r2)
                RequestChunkUnload(coord);
        }
    }

    public ChunkLiteBurst GetChunk(Vector2Int coord) 
        => m_activeChunks.TryGetValue(coord, out var chunk) ? chunk : null;

    #endregion
    #region BatchControl
    private void ProcessBatch() {
        ProcessUnloads();
        ProcessLoads();
        // ProcessModifies();
    }

    private void ProcessUnloads() {
        if (m_pendingUnloads.Count == 0) return;

        _ = ChunkStorage.SaveManyAsync(m_pendingUnloads);

        foreach (ChunkLiteBurst chunk in m_pendingUnloads) {
            m_activeChunks.Remove(chunk.Coords);
            m_chunkPool.Return(chunk);
        }

        m_pendingUnloads.Clear();
    }

    private async void ProcessLoads() {
        if (m_pendingLoads.Count == 0) return;

        HashSet<ChunkLiteBurst> chunksToLoad = m_pendingLoads;
        m_pendingLoads = new HashSet<ChunkLiteBurst>();

        await ChunkStorage.LoadManyAsync(chunksToLoad);

        foreach (ChunkLiteBurst chunk in chunksToLoad) {
            if (chunk.IsLoaded) {
                m_generationQueue.Enqueue(new ChunkGenerationRequest {
                    chunk           = chunk,
                    jobHandle       = default
                });
                continue;
            }

            var job = new GenerateTerrainJob {
                chunkCoords = new int2(chunk.Coords.x, chunk.Coords.y),
                resolution  = m_chunkResolution,
                paddedSize  = PaddedSize,
                scale       = m_noiseScale,
                seed        = m_seed,
                heightMap   = chunk.HeightMap
            };

            JobHandle handle = job.Schedule(chunk.HeightMap.Length, 64);
            m_activeJobHandles.Add(handle);

            m_generationQueue.Enqueue(new ChunkGenerationRequest {
                chunk           = chunk,
                jobHandle       = handle
            });

            chunk.IsLoaded = true;
        }

        m_pendingLoads.Clear();
    }

    #endregion
    #region Generation
    private void ProcessGenerationQueue() {
        int processed = 0;

        while (m_generationQueue.Count > 0 && processed < m_maxChunksPerFrame) {
            var request = m_generationQueue.Peek();
            if (!request.jobHandle.IsCompleted) break;

            ChunkLiteBurst chunk = request.chunk;
            Vector2Int coords = chunk.Coords;

            m_generationQueue.Dequeue();
            request.jobHandle.Complete();

            if (IsInnerChunk(chunk)) {
                chunk.HasMesh = true;
                ChunkMeshCreatorBurst.Instance.GenerateMesh(chunk);
            }

            m_activeChunks[coords] = chunk;
            m_loadingChunks.Remove(coords);
            processed++;
        }
    }

    #endregion
    #region Helpers
    private bool HasChunkAnywhere(Vector2Int coord) 
        => m_activeChunks.ContainsKey(coord) || m_loadingChunks.ContainsKey(coord);

    private ChunkLiteBurst GetChunkAnywhere(Vector2Int coord) {
        if (m_activeChunks.TryGetValue(coord,  out var chunk)) return chunk;
        if (m_loadingChunks.TryGetValue(coord, out chunk))     return chunk;
        return null;
    }

    private bool IsInnerChunk(ChunkLiteBurst chunk) {
        // TODO : make the logic
        return true;
    }

    #endregion
    #region PublicUtils
    public Vector2Int WorldToChunk(Vector3 world) => new(
        x: Mathf.FloorToInt(world.x / m_chunkSize),
        y: Mathf.FloorToInt(world.z / m_chunkSize)
    );

    public Vector2Int WorldToLocal(Vector3 world) {
        int cx = Mathf.FloorToInt(world.x / m_chunkSize);
        int cy = Mathf.FloorToInt(world.z / m_chunkSize);
        return new Vector2Int(
            x: Mathf.FloorToInt(world.x) - cx * m_chunkSize + 1,
            y: Mathf.FloorToInt(world.z) - cy * m_chunkSize + 1
        );
    }

    #endregion
    #region Debug
    public void DebugInfo() {
        Debug.Log($"Active Chunks:      {m_activeChunks.Count}      ");
        Debug.Log($"Loading Chunks:     {m_loadingChunks.Count}     ");
        Debug.Log($"Pool Size:          {m_chunkPool.PoolCount}     ");
        Debug.Log($"Pending Loads:      {m_pendingLoads.Count}      ");
        Debug.Log($"Pending Unloads:    {m_pendingUnloads.Count}    ");
        Debug.Log($"Pending Modifies:   {m_pendingModifies.Count}   ");
        Debug.Log($"Gen Queue:          {m_generationQueue.Count}   ");
        Debug.Log($"Active Jobs:        {m_activeJobHandles.Count}  ");
    }

    #endregion
}