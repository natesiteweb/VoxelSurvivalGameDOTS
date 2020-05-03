using System.Collections;
using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using System.Linq;

public class LODManagerTWOD : MonoBehaviour
{
    public GameObject chunkPrefab;
    public GameObject mouseFollow;

    [SerializeField]
    private byte totalChunks;

    [SerializeField]
    private int worldSize = 128;

    [SerializeField]
    private int baseChunkSize = 4;

    [SerializeField]
    private int chunkSize;

    [SerializeField]
    private int LODActualSize;

    [SerializeField]
    private byte maxLOD;


    Dictionary<int3, twodChunk> chunkMap = new Dictionary<int3, twodChunk>(2000);
    List<twodChunk> chunkUpdateList = new List<twodChunk>(2000);

    float3 mousePos;

    void Start()
    {
        totalChunks = 1;
        maxLOD = (byte)Mathf.CeilToInt(Mathf.Log(worldSize, 2f) - Mathf.Log(baseChunkSize, 2f));

        LODActualSize = (int)Mathf.Pow(2f, maxLOD + Mathf.Log(baseChunkSize, 2f));

        GameObject firstPrefab = Instantiate(chunkPrefab);
        firstPrefab.transform.localScale = new float3(worldSize, worldSize, worldSize);
        firstPrefab.transform.position = new float3(0f, 0f, 0f);
        firstPrefab.transform.GetChild(0).GetComponent<MeshRenderer>().material.color = Color.red;

        twodChunk chunkData = firstPrefab.GetComponent<twodChunk>();

        chunkData.lod = maxLOD;
        chunkData.chunkSize = baseChunkSize;
        chunkData.actualChunkSize = baseChunkSize * (int)Mathf.Pow(2f, chunkData.lod);
        chunkData.actualChunkSizeHalf = chunkData.actualChunkSize / 2;
        chunkData.chunkPos = int3.zero;

        chunkData.parentList.Add(int3.zero);

        chunkMap.Add(int3.zero, chunkData);
        chunkUpdateList.Add(chunkData);
    }

    void Update()
    {
        mousePos = new float3(Camera.main.ScreenToWorldPoint(Input.mousePosition).x, 0f, Camera.main.ScreenToWorldPoint(Input.mousePosition).z);
        mouseFollow.transform.position = mousePos;

        for(int i = 0; i < chunkUpdateList.Count; i++)
        {
            twodChunk chunk = chunkUpdateList[i];

            Vector3 center = Vector3.one * chunk.actualChunkSizeHalf;
            Bounds bounds = new Bounds(center + chunk.transform.position, Vector3.one * chunk.actualChunkSize);
            float sqrDistance = bounds.SqrDistance(mousePos);

            if (ShouldSplitDistance(chunk, sqrDistance) || chunk.markedToSplit)
            {
                SplitChunk(chunk);
            }
        }
    }

    void SplitChunk(twodChunk chunk)
    {
        chunkMap.Remove(chunk.chunkPos);
        chunkUpdateList.Remove(chunk);

        ShouldSplitLOD(chunk);

        totalChunks += 3;

        for (int x = 0; x < 2; x++)
        {
            for (int y = 0; y < 2; y++)
            {
                GameObject firstPrefab = Instantiate(chunkPrefab);

                twodChunk chunkData = firstPrefab.GetComponent<twodChunk>();

                chunkData.lod = (byte)(chunk.lod - 1);
                chunkData.chunkSize = baseChunkSize;
                chunkData.actualChunkSize = baseChunkSize * (int)Mathf.Pow(2f, chunkData.lod);
                chunkData.actualChunkSizeHalf = chunkData.actualChunkSize / 2;
                chunkData.chunkPos = new int3(x * chunkData.actualChunkSize, 0, y * chunkData.actualChunkSize) + chunk.chunkPos;
                firstPrefab.transform.GetChild(0).GetComponent<MeshRenderer>().material.color = Color.red;

                firstPrefab.transform.localScale = new float3(chunkData.actualChunkSize, chunkData.actualChunkSize, chunkData.actualChunkSize);
                firstPrefab.transform.position = new float3(x * chunkData.actualChunkSize, 0f, y * chunkData.actualChunkSize) + chunk.chunkPos;

                int3[] tempList = (int3[])chunk.parentList.ToArray().Clone();
                chunkData.parentList = tempList.ToList();
                chunkData.parentList.Add(new int3(x, 0, y));

                chunkMap.Add(chunkData.chunkPos, chunkData);
                chunkUpdateList.Add(chunkData);
            }
        }

        Destroy(chunk.gameObject);
    }

    bool ShouldSplitDistance(twodChunk chunk, float distance)
    {
        bool returnValue = false;

        float checkDist = (float)chunk.actualChunkSize / 2f;

        if (distance < (checkDist * checkDist) && chunk.lod > 0)
        {
            returnValue = true;
        }

        return returnValue;
    }

    int3 parentPos;
    bool needToSplit = true;

    void ShouldSplitLOD(twodChunk chunk)
    {
        if (chunk.lod < maxLOD)
        {
            parentPos = chunk.chunkPos - chunk.parentList[chunk.parentList.Count - 1] * chunk.actualChunkSize;

            int3[] sides = GetAdjacentSides(chunk.chunkPos, chunk.actualChunkSize, chunk.parentList[chunk.parentList.Count - 1]);

            for (int i = 0; i < sides.Length; i++)
            {
                needToSplit = true;

                if (chunkMap.ContainsKey(sides[i]) && chunkMap[sides[i]].lod <= chunk.lod)
                {
                    needToSplit = false;
                    //Debug.Log("DONT SPLIT");
                }
                else
                {
                    //Debug.Log("WHY SPLIT");
                }

                if (needToSplit && IsInBounds(GetParentChunk(sides[i], chunk.actualChunkSize * 2), LODActualSize) && chunkMap.ContainsKey(GetParentChunk(sides[i], chunk.actualChunkSize * 2)))
                {
                    //chunkMap[GetParentChunk(sides[i], chunk.values.actualChunkSize * 2)].markedToSplit = true;
                    chunkMap[GetParentChunk(sides[i], chunk.actualChunkSize * 2)].transform.GetChild(0).GetComponent<MeshRenderer>().material.color = Color.yellow;
                    chunkMap[GetParentChunk(sides[i], chunk.actualChunkSize * 2)].markedToSplit = true;
                    Debug.Log("SPLIT");
                }
            }
        }
    }

    static int3[] GetAdjacentSides(int3 chunkPos, int realChunkSize, int3 localPos)
    {
        int3[] result = new int3[2];

        int x = (int)(localPos.x * 2f) - 1;
        int y = (int)(localPos.y * 2f) - 1;
        int z = (int)(localPos.z * 2f) - 1;

        result[0] = new int3(chunkPos.x + (x * realChunkSize), 0, chunkPos.z);
        //result[1] = new int3(chunkPos.x, chunkPos.x + (y * realChunkSize), chunkPos.z);
        result[1] = new int3(chunkPos.x, 0, chunkPos.z + (z * realChunkSize));

        return result;
    }

    static int3 GetParentChunk(float3 chunkPos, int realChunkSize)
    {
        return new int3(Mathf.FloorToInt(chunkPos.x / realChunkSize) * realChunkSize, Mathf.FloorToInt(chunkPos.y / realChunkSize) * realChunkSize, Mathf.FloorToInt(chunkPos.z / realChunkSize) * realChunkSize);
    }

    static bool IsInBounds(int3 pos, int LODActualSize)
    {
        if (pos.x <= LODActualSize && pos.x >= 0f && pos.y <= LODActualSize && pos.y >= 0f && pos.z <= LODActualSize && pos.z >= 0f)
        {
            return true;
        }
        else
            return false;
    }
}
