using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;

public class twodChunk : MonoBehaviour
{
    public byte lod;
    public int chunkSize;
    public int actualChunkSize;
    public int actualChunkSizeHalf;
    public int3 chunkPos;
    public bool markedToSplit = false;

    public List<int3> parentList = new List<int3>();
}
