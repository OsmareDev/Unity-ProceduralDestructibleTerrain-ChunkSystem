using System.Collections.Generic;
using UnityEngine;

public class ChunkStreamerBurst : SceneSingleton<ChunkStreamerBurst> {
    [Header("References")] // ───────────────────────────────────────────────────────────
    [SerializeField] private Transform  m_player;
    
    [Header("Streaming Settings")] // ───────────────────────────────────────────────────
    [SerializeField] private int        m_viewDistance          = 5;
    [SerializeField] private float      m_updateInterval        = 0.5f;
    [SerializeField] private int        m_maxChunksPerUpdate    = 8;

    // ──────────────────────────────────────────────────────────────────────────────────
    // INTERNAL STATE ───────────────────────────────────────────────────────────────────
    private Vector2Int                  m_lastPlayerChunk;
    private float                       m_timeSinceLastUpdate;
    private HashSet<Vector2Int>         m_requiredChunks        = new HashSet<Vector2Int>();
    private List<Vector2Int>            m_toLoad                = new();

    #region UnityLyfecycle
    void Start() {
        if (m_player == null) m_player = Camera.main.transform;

        m_lastPlayerChunk = ChunkManagerLiteBurst.Instance.WorldToChunk(m_player.position);
        UpdateChunks();
    }

    void Update() {
        m_timeSinceLastUpdate += Time.deltaTime;
        if (m_timeSinceLastUpdate < m_updateInterval) return;
        m_timeSinceLastUpdate = 0f;

        Vector2Int currentChunk = ChunkManagerLiteBurst.Instance.WorldToChunk(m_player.position);
        if (currentChunk == m_lastPlayerChunk) return;

        m_lastPlayerChunk = currentChunk;
        UpdateChunks();
    }

    #endregion

    #region Streaming
    private void UpdateChunks() {
        BuildRequiredChunks();
        RequestLoads();
        ChunkManagerLiteBurst.Instance.UnloadOutsideRadius(m_lastPlayerChunk, m_viewDistance);
    }

    private void BuildRequiredChunks() {
        m_requiredChunks.Clear();

        for (int y = -m_viewDistance; y <= m_viewDistance; y++)
        for (int x = -m_viewDistance; x <= m_viewDistance; x++) {
            if (x * x + y * y <= m_viewDistance * m_viewDistance)
                m_requiredChunks.Add(m_lastPlayerChunk + new Vector2Int(x, y));
        }
    }

    private void RequestLoads() {
        m_toLoad.Clear();
        m_toLoad.AddRange(m_requiredChunks);

        // Sort by squared distance — no sqrt needed
        m_toLoad.Sort((a, b) => {
            Vector2Int da = a - m_lastPlayerChunk;
            Vector2Int db = b - m_lastPlayerChunk;
            return (da.x * da.x + da.y * da.y)
                  .CompareTo(db.x * db.x + db.y * db.y);
        });

        foreach (var coord in m_toLoad)
            ChunkManagerLiteBurst.Instance.RequestChunkLoad(coord);
    }

    #endregion

    #region Debug
    void OnDrawGizmosSelected() {
        return;
        if (m_player == null) return;

        Vector2Int playerChunk = ChunkManagerLiteBurst.Instance.WorldToChunk(m_player.position);
        int chunkSize = ChunkManagerLiteBurst.Instance.ChunkSize;

        // Draw view distance
        Gizmos.color = Color.yellow;
        float radius = m_viewDistance * chunkSize;
        Gizmos.DrawWireSphere(new Vector3(
            playerChunk.x * chunkSize + chunkSize * 0.5f,
            0,
            playerChunk.y * chunkSize + chunkSize * 0.5f
        ), radius);

        // Draw required chunks
        Gizmos.color = Color.green;
        foreach (var coord in m_requiredChunks) {
            Vector3 center = new Vector3(
                coord.x * chunkSize + chunkSize * 0.5f,
                0,
                coord.y * chunkSize + chunkSize * 0.5f
            );
            Gizmos.DrawWireCube(center, new Vector3(chunkSize, 1, chunkSize));
        }
    }

    #endregion
}
