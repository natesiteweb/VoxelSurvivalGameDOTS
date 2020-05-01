using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public struct ChunkComponent : IComponentData
{
    public int lod;
    public int chunkSize;
    public int actualSize;
    public int actualSizeHalf;
    public float3 chunkPos;
}

public class ChunkGenerateTag : IComponentData
{

}

[InternalBufferCapacity(24)]
public struct ParentPosBuffer : IBufferElementData
{
    float3 Value;
}

[InternalBufferCapacity(32768)]
public struct DensityBuffer : IBufferElementData
{
    float Value;
}

[InternalBufferCapacity(491520)]
public struct Vertex : IBufferElementData
{
    public float3 Value;
}

[InternalBufferCapacity(491520)]
public struct Uv : IBufferElementData
{
    public float3 Value;
}

[InternalBufferCapacity(491520)]
public struct Normal : IBufferElementData
{
    public float3 Value;
}

[InternalBufferCapacity(491520)]
public struct Triangle : IBufferElementData
{
    public int Value;
}

public struct EntityHolder : IBufferElementData
{
    public Entity entity;
}
