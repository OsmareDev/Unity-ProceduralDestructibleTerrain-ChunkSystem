using Unity.Collections;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Burst;

[BurstCompile]
public struct GenerateTerrainJob : IJobParallelFor {
    [ReadOnly] public int2 chunkCoords;
    [ReadOnly] public int resolution;
    [ReadOnly] public int paddedSize;
    [ReadOnly] public float scale;
    [ReadOnly] public int seed;
    
    [WriteOnly] public NativeArray<float> heightMap;

    public void Execute(int index) {
        int totalSize = paddedSize;
        int y = index / totalSize;
        int x = index % totalSize;

        int gridX = x - 1;
        int gridY = y - 1;

        int worldX = chunkCoords.x * (resolution - 1) + gridX;
        int worldY = chunkCoords.y * (resolution - 1) + gridY;

        float height = noise.cnoise(new float2(worldX * scale, worldY * scale) + seed) * 0.5f + 0.5f;
        height *= resolution;

        heightMap[index] = height;
    }
}

[BurstCompile]
public struct ModifyHeightJob : IJobParallelFor {
    [ReadOnly] public float2 center;
    [ReadOnly] public float radius;
    [ReadOnly] public float strength;
    [ReadOnly] public int2 chunkCoords;
    [ReadOnly] public int resolution;
    [ReadOnly] public int paddedSize;
    
    public NativeArray<float> heightMap;

    private int Idx(int x, int y) {
        return (y + 1) * paddedSize + (x + 1);
    }

    public void Execute(int index) {
        int y = index / resolution;
        int x = index % resolution;

        float2 worldPos = new float2(
            chunkCoords.x * (resolution - 1) + x,
            chunkCoords.y * (resolution - 1) + y
        );

        float2 diff = worldPos - center;
        float dist = math.length(diff);

        if (dist < radius) {
            float falloff = 1f - (dist / radius);
            falloff = falloff * falloff; // Smooth falloff
            
            int idx = Idx(x, y);
            heightMap[idx] += strength * falloff;
        }
    }
}