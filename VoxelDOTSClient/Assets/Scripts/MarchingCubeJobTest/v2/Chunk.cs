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
    public float[] densities;
    public Chunk[] childrenFromSplit = new Chunk[8];
    public List<Chunk> childrenFromJoin = new List<Chunk>(8);

    public Chunk parentFromSplit;
    public int childrenLoadStatus;

    public Planet planet;

    void Start()
    {

    }

    void OnApplicationQuit()
    {
        
    }

    public int CompareTo(Chunk chunk)
    {
        return values.distanceFromPlayer.CompareTo(chunk.values.distanceFromPlayer);
    }

    private void OnDrawGizmos()
    {
        if (planet != null && values.drawBounds && planet.drawChunkBounds)
        {
            Gizmos.color = values.wireColor;
            Gizmos.DrawWireCube(new Vector3(values.actualChunkSizeHalf, values.actualChunkSizeHalf, values.actualChunkSizeHalf) + transform.position, new Vector3(values.actualChunkSize, values.actualChunkSize, values.actualChunkSize));
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
    public float distanceFromPlayer;
    public bool drawBounds;
    public float vertexScale;

    public bool isEmpty;
    public bool isFull;
    public bool isDoneLoading;
    public bool markedToSplit;
    public bool hasSplit;
    public bool hasJoined;
    public bool isChild;

    public int indexForUpdate;

    public byte indexFromSplit;

    public Color wireColor;

    //public int3[] parentList;
    //public float[] densities;
}