using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;

public class Chunk : MonoBehaviour
{
    //public NativeArray<float> densities;

    public ChunkValues values;

    public int3[] parentList;
    //public float[] densities;
    public NativeArray<float> densities;
    public int3[] borderChunkTrans = new int3[6];
    public Chunk[] childrenFromSplit = new Chunk[8];
    //public TransChunk[] transChunks = new TransChunk[6];
    public GameObject borderRingChunk;
    public byte transChunksShouldLoadCheck = 0;
    public byte transChunksLoaded = 0;
    public List<Chunk> childrenFromJoin = new List<Chunk>(8);

    public Chunk parentFromSplit;
    public int childrenLoadStatus;

    public Planet planet;

    void Start()
    {

    }

    void OnApplicationQuit()
    {
        if(densities.IsCreated)
            densities.Dispose();
    }

    public int CompareTo(Chunk chunk)
    {
        return values.distanceFromPlayer.CompareTo(chunk.values.distanceFromPlayer);
    }

    private void OnDrawGizmos()
    {
        if (planet != null && values.drawBounds && planet.drawChunkBounds)
        {
            Matrix4x4 rotationMatrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
            Gizmos.matrix = rotationMatrix;

            Gizmos.color = values.wireColor;
            Gizmos.DrawWireCube(new Vector3(values.actualChunkSizeHalf, values.actualChunkSizeHalf, values.actualChunkSizeHalf), new Vector3(values.actualChunkSize, values.actualChunkSize, values.actualChunkSize));
        }
    }

    static int to1D(int3 ids, int3 dim)
    {
        return (dim.y * dim.z * ids.x) + (dim.z * ids.y) + ids.z;
    }

    public float GetDensity(int3 pos)
    {
        if(densities.IsCreated)
        {
            return densities[to1D(pos, values.chunkSize + new int3(3))];
        }
        else
        {
            densities = new NativeArray<float>((values.chunkSize + 3) * (values.chunkSize + 3) * (values.chunkSize + 3), Allocator.Persistent);

            return 0f;
        }
    }

    public void SetDensity(int3 pos, float density)
    {
        if(densities.IsCreated)
        {
            densities[to1D(pos, values.chunkSize + new int3(3))] = density;
        }
        else
        {
            densities = new NativeArray<float>((values.chunkSize + 3) * (values.chunkSize + 3) * (values.chunkSize + 3), Allocator.Persistent);

            densities[to1D(pos, values.chunkSize + new int3(3))] = density;
        }
    }
}

[System.Serializable]
public struct ChunkValues
{
    public byte lod;
    public int chunkSize;
    public int actualChunkSize;
    public int actualChunkSizeHalf;
    public int3 chunkPos;
    public int3 lastParentList;
    public float distanceFromPlayer;
    public bool drawBounds;
    public float vertexScale;

    public bool isEmpty;
    public bool isFull;
    public bool isDoneLoading;
    public bool markedToSplit;
    public bool markedToUpdateTransvoxel;
    public bool hasSplit;
    public bool hasJoined;
    public bool isChild;

    public int indexForUpdate;

    public byte indexFromSplit;

    public Color wireColor;

    //public int3[] parentList;
    //public float[] densities;
}