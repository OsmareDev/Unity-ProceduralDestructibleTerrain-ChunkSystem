using UnityEngine;
using Unity.Mathematics;

public struct VertexData {
    public Vector3 position;
    public Vector3 normal;
    public int2 id;
}

public struct Triangle {
    public VertexData vertexC;
    public VertexData vertexB;
    public VertexData vertexA;
};