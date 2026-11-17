using UnityEngine;

public static class ComputeHelpers {
    public static void CreateStructuredBuffer<T>(ref ComputeBuffer buffer, int count) {
        int stride = GetStride<T>();

        bool needsNewBuffer = buffer == null || !buffer.IsValid() || buffer.count != count || buffer.stride != stride;
        if ( needsNewBuffer ) {
            TryReleasing(buffer);
            buffer = new ComputeBuffer( count, stride );
        }
    }

    public static void Dispatch(ComputeShader cs, int xIterations, int yIterations = 1, int zIterations = 1, int kernelIndex = 0) {
        Vector3Int threadGroupSizes = GetThreadGroupSizes(cs, kernelIndex);

        int numGroupsX = Mathf.CeilToInt(xIterations / (float)threadGroupSizes.x);
		int numGroupsY = Mathf.CeilToInt(yIterations / (float)threadGroupSizes.y);
		int numGroupsZ = Mathf.CeilToInt(zIterations / (float)threadGroupSizes.y);

        cs.Dispatch(kernelIndex, numGroupsX, numGroupsY, numGroupsZ);
    }

    public static void TryReleasing(params ComputeBuffer[] buffers) {
        for ( int i = 0; i < buffers.Length; i++ ) {
            if (buffers[i] != null) buffers[i].Release();
        }
    }

    public static int GetStride<T>() => System.Runtime.InteropServices.Marshal.SizeOf(typeof(T));
    
    public static Vector3Int GetThreadGroupSizes( ComputeShader cs, int kernelIndex ) {
        cs.GetKernelThreadGroupSizes(kernelIndex, out uint x, out uint y, out uint z);
        return new Vector3Int((int)x, (int)y, (int)z);
    } 
}
