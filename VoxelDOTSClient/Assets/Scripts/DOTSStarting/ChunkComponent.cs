using Unity.Entities;
using Unity.Mathematics;

public struct ChunkComponent : IComponentData
{
    public int lod;
    public int chunkSize;
    public int actualSize;
    public int actualSizeHalf;
    public float tempMoveSpeed;
    public float2 offset;
}

[InternalBufferCapacity(0)]
public struct Vertex : IBufferElementData
{
    public float3 Value;
}

[InternalBufferCapacity(0)]
public struct Uv : IBufferElementData
{
    public float3 Value;
}

[InternalBufferCapacity(0)]
public struct Normal : IBufferElementData
{
    public float3 Value;
}

/*[InternalBufferCapacity(0)]
public struct Triangle : IBufferElementData
{
    public int Value;
}
*/