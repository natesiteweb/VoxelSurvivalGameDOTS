using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Jobs;
using Unity.Rendering;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.UI;
using System.Linq;

public class Planet : MonoBehaviour
{
    [Header("Voxel Info")]
    public int baseChunkSize = 16;
    public float isoValue = 0.5f;
    public byte maxLOD;
    public byte minLOD;
    public int LODActualSize;
    public bool drawPlanetBounds = true;
    public bool drawChunkBounds = true;
    public int batchSize = 32;
    public int threadCount = 2;
    public bool smoothNormals;

    public int poolSize = 2000;

    [Header("Planet Settings")]
    public float radius;                        //CANNOT BE SMALLER THAN baseChunkSize
    public FastNoiseSIMDUnity noise1;
    public FastNoiseSIMDUnity noise2;
    public int chunksGenerated = 0;
    public Text chunkLoadedText;
    public Text playerVelText;

    public GameObject playerObj;

    Dictionary<Chunk, Mesh> meshMap;
    List<Chunk> loadQueue;
    List<Chunk> logicUpdateList;

    Dictionary<int3, Chunk> chunkMap;

    public Queue<GameObject> chunkPool;

    GameObject chunkPrefab;

    NativeArray<float> densities;
    NativeArray<float3> curVertices;
    NativeArray<float3> curNormals;
    NativeArray<int> curTriangles;

    NativeArray<int> nbTriTable;
    NativeArray<int> triTable;

    int totalSize;
    float dx;
    float3 originGrid;

    Material material;

    float2 tempOffset = float2.zero;

    float[] generationData;
    float[] generationData2;

    void Awake()
    {
        meshMap = new Dictionary<Chunk, Mesh>(poolSize);
        //chunkList = new NativeList<Entity>(poolSize, Allocator.Persistent);
        //loadQueue = new NativeQueue<Entity>(Allocator.Persistent);

        loadQueue = new List<Chunk>(poolSize);
        chunkMap = new Dictionary<int3, Chunk>(poolSize);
        logicUpdateList = new List<Chunk>(poolSize);

        chunkPool = new Queue<GameObject>(poolSize);

        totalSize = (baseChunkSize + 1) * (baseChunkSize + 1) * (baseChunkSize + 1);
        generationData = new float[totalSize];

        originGrid = float3.zero;
        dx = 1;
        material = Resources.Load<Material>("Materials/TerrainMat");
        chunkPrefab = Resources.Load<GameObject>("Prefabs/chunkPrefab");
        initTriTable();

        maxLOD = (byte)Mathf.CeilToInt(Mathf.Log(radius * 2f, 2f) - Mathf.Log(baseChunkSize, 2f));
        minLOD = (byte)0;

        LODActualSize = (int)Mathf.Pow(2f, maxLOD + Mathf.Log(baseChunkSize, 2f));

        transform.position = new Vector3(-LODActualSize / 2f, -LODActualSize / 2f - radius, -LODActualSize / 2f);

        for (int i = 0; i < poolSize; i++)
        {
            GameObject spawnedChunk = Instantiate(chunkPrefab);
            spawnedChunk.GetComponent<Chunk>().enabled = false;
            spawnedChunk.transform.SetParent(transform);
            chunkPool.Enqueue(spawnedChunk);

            spawnedChunk.GetComponent<Chunk>().values.isDoneLoading = false;
            spawnedChunk.GetComponent<Chunk>().values.wireColor = Color.red;

            Mesh newMesh = new Mesh();
            newMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            spawnedChunk.GetComponent<MeshFilter>().mesh = newMesh;
            spawnedChunk.GetComponent<MeshCollider>().sharedMesh = newMesh;

            //meshMap.Add(spawnedChunk.GetComponent<Chunk>(), newMesh);
        }
    }

    void Start()
    {
        for (int i = 0; i < 1; i++) //For loop was for debugging
        {
            GameObject tempChunk = GetChunk();

            tempChunk.transform.localPosition = float3.zero;

            Chunk chunk = tempChunk.GetComponent<Chunk>();

            chunk.values.chunkPos = int3.zero;
            chunk.values.lod = maxLOD;
            chunk.values.chunkSize = baseChunkSize;
            chunk.values.actualChunkSize = baseChunkSize * (int)Mathf.Pow(2f, chunk.values.lod);
            chunk.values.actualChunkSizeHalf = chunk.values.actualChunkSize / 2;
            chunk.values.vertexScale = chunk.values.actualChunkSize / baseChunkSize;
            chunk.values.drawBounds = true;
            chunk.values.markedToSplit = false;
            chunk.values.distanceFromPlayer = Mathf.Infinity;

            chunk.parentList = new int3[1];
            chunk.parentList[0] = int3.zero;

            chunk.planet = this;

            //tempChunk.transform.position = new Vector3(0f, 0f, i * baseChunkSize);

            chunkMap.Add(new int3(i, 0, 0), chunk);
            loadQueue.Add(chunk);
            logicUpdateList.Add(chunk);
        }
    }

    void OnApplicationQuit()
    {
        Clean();

        if (triTable.IsCreated)
            triTable.Dispose();

        if (nbTriTable.IsCreated)
            nbTriTable.Dispose();
    }

    float updateTimer = 0f;
    float logicTimer = 0f;
    float logicTime = .001f;

    int startUpdateLength;

    Chunk chunkForJob;

    void Update()
    {
        updateTimer -= Time.deltaTime;
        logicTimer -= Time.deltaTime;

        for (int i = 0; i < threadCount; i++)
        {
            if (updateTimer <= 0f && loadQueue.Count > 0)
            {
                loadQueue = loadQueue.OrderBy(w => w.values.distanceFromPlayer).ToList();

                updateTimer = 0f;

                float startTime = Time.realtimeSinceStartup;

                GenerateMesh(loadQueue[0]);

                loadQueue[0] = loadQueue[loadQueue.Count - 1];

                loadQueue.RemoveAt(loadQueue.Count - 1);

                //GenerateMesh(loadQueue.Dequeue());

                //Debug.Log("Took: " + (Time.realtimeSinceStartup - startTime) * 1000f + "ms");
            }
        }

        chunkLoadedText.text = chunksGenerated.ToString();

        if (playerObj.GetComponent<Rigidbody>().velocity.magnitude > 800f)
        {
            logicTime = 1f;
            minLOD = 6;
        }
        else if (playerObj.GetComponent<Rigidbody>().velocity.magnitude > 400f)
        {
            logicTime = 0.5f;
            minLOD = 3;
        }
        else if (playerObj.GetComponent<Rigidbody>().velocity.magnitude > 100f)
        {
            logicTime = 0.05f;
            minLOD = 1;
        }
        else
        {
            logicTime = 0.01f;
            minLOD = 0;
        }

        if (logicUpdateList.Count > 0 && logicTimer <= 0f)
        {
            logicTimer = logicTime;
            ChunkUpdateLogic();
        }

        //Debug.Log("asdasd");
    }

    Chunk curChunk;
    float3 center;
    float3 playerPos;
    float3 playerVel;
    float sqrDistance;
    bool childrenAreDone;
    bool siblingsSameLOD;
    int3 tempVector;
    Vector3 parentCenter;
    Bounds parentBounds;

    void ChunkUpdateLogic()
    {
        float startTime = Time.realtimeSinceStartup;

        playerPos = playerObj.transform.position;
        playerVel = playerObj.GetComponent<Rigidbody>().velocity * 2f;

        playerVelText.text = playerObj.GetComponent<Rigidbody>().velocity.magnitude.ToString();

        for (int i = 0; i < logicUpdateList.Count; i++)
        {
            curChunk = logicUpdateList[i];

            Vector3 center = Vector3.one * curChunk.values.actualChunkSizeHalf;
            Bounds bounds = new Bounds(center + curChunk.transform.position, Vector3.one * curChunk.values.actualChunkSize);

            sqrDistance = bounds.SqrDistance(playerPos + playerVel);
            curChunk.values.distanceFromPlayer = sqrDistance;

            if (!curChunk.values.hasJoined && !curChunk.values.hasSplit && (curChunk.parentList[curChunk.parentList.Length - 1].x == 0 && curChunk.parentList[curChunk.parentList.Length - 1].y == 0 && curChunk.parentList[curChunk.parentList.Length - 1].z == 0) && curChunk.values.lod < maxLOD && curChunk.parentFromSplit == null)
            {
                siblingsSameLOD = true;

                for (int x = 0; x < 2; x++)
                {
                    for (int y = 0; y < 2; y++)
                    {
                        for (int z = 0; z < 2; z++)
                        {
                            tempVector = new int3(x, y, z);

                            if (chunkMap[tempVector * curChunk.values.actualChunkSize + curChunk.values.chunkPos].values.lod != curChunk.values.lod || !chunkMap[tempVector * curChunk.values.actualChunkSize + curChunk.values.chunkPos].values.isDoneLoading || chunkMap[tempVector * curChunk.values.actualChunkSize + curChunk.values.chunkPos].values.hasJoined || chunkMap[tempVector * curChunk.values.actualChunkSize + curChunk.values.chunkPos].values.hasSplit)
                            {
                                siblingsSameLOD = false;
                            }
                        }
                    }
                }

                if (siblingsSameLOD)
                {
                    parentCenter = Vector3.one * curChunk.values.actualChunkSize;
                    parentBounds = new Bounds(parentCenter + curChunk.transform.position, Vector3.one * curChunk.values.actualChunkSize * 2f);

                    float parentSqrDistance = parentBounds.SqrDistance(playerPos + playerVel);

                    if (ShouldJoinDistance(parentSqrDistance, curChunk.values.actualChunkSize))
                    {
                        curChunk.values.hasJoined = false;

                        JoinChunks(curChunk);
                        //continue;
                    }
                }
            }

            if(!curChunk.values.hasSplit && curChunk.values.hasJoined && curChunk.values.isDoneLoading)
            {
                curChunk.values.hasJoined = false;

                while (curChunk.childrenFromJoin.Count > 0)
                {
                    Chunk tempObj = curChunk.childrenFromJoin[curChunk.childrenFromJoin.Count - 1];

                    tempObj.GetComponent<MeshCollider>().enabled = false;
                    tempObj.GetComponent<MeshRenderer>().enabled = false;

                    curChunk.childrenFromJoin.Remove(tempObj);

                    RemoveChunk(tempObj);
                }

                curChunk.childrenFromJoin.Clear();
            }

            if (curChunk.values.hasSplit)
            {
                childrenAreDone = true;

                for (int j = 0; j < 8; j++)
                {
                    if (!curChunk.childrenFromSplit[j].values.isDoneLoading)
                    {
                        childrenAreDone = false;
                    }
                }

                if (childrenAreDone)
                {
                    chunkMap.Remove(curChunk.values.chunkPos);

                    for (int j = 0; j < 8; j++)
                    {
                        curChunk.childrenFromSplit[j].parentFromSplit = null;
                        curChunk.childrenFromSplit[j].GetComponent<MeshRenderer>().enabled = true;
                        curChunk.childrenFromSplit[j].GetComponent<MeshCollider>().enabled = true;

                        chunkMap.Add(curChunk.childrenFromSplit[j].values.chunkPos, curChunk.childrenFromSplit[j]);
                        logicUpdateList.Add(curChunk.childrenFromSplit[j]);
                    }

                    curChunk.GetComponent<MeshCollider>().enabled = false;
                    curChunk.GetComponent<MeshRenderer>().enabled = false;

                    logicUpdateList.RemoveAt(i);

                    RemoveChunk(curChunk);
                }
            }

            if (curChunk.values.isDoneLoading && ((curChunk.parentFromSplit == null && curChunk.values.isChild) || !curChunk.values.isChild) && !curChunk.values.hasSplit && !curChunk.values.hasJoined && (!curChunk.values.isEmpty && !curChunk.values.isFull))
            {
                if (curChunk.values.markedToSplit || ShouldSplitDistance(sqrDistance, curChunk.values.actualChunkSize, curChunk.values.isEmpty, curChunk.values.isFull, minLOD, curChunk.values.lod))
                {
                    curChunk.values.hasSplit = true;
                    SplitChunk(curChunk, i);
                    //ShouldSplitLOD()
                }
            }
        }

        //Debug.Log("Logic: " + (Time.realtimeSinceStartup - startTime) * 1000f + "ms");
    }

    byte isMeshEmpty;

    void GenerateMesh(Chunk entity)
    {
        //entity.values.drawBounds = false;

        //EntityManager.RemoveComponent(entity, typeof(ChunkGenerateTag));

        isMeshEmpty = 0;

        //Mesh mesh = meshMap[entity];

        if (curVertices.IsCreated)
            curVertices.Dispose();
        if (curNormals.IsCreated)
            curNormals.Dispose();
        if (curTriangles.IsCreated)
            curTriangles.Dispose();

        //CountVertexPerVoxelJob
        NativeArray<uint2> vertPerCellIn = new NativeArray<uint2>(totalSize, Allocator.TempJob);
        NativeArray<uint2> vertPerCell = new NativeArray<uint2>(totalSize, Allocator.TempJob);
        NativeArray<uint> compactedVoxel = new NativeArray<uint>(totalSize, Allocator.TempJob);

        isMeshEmpty = GenerateTerrainData(entity);

        chunksGenerated++;

        if (isMeshEmpty == 1)
        {
            entity.values.isEmpty = true;
            entity.values.isFull = false;
            entity.values.isDoneLoading = true;

            vertPerCell.Dispose();
            compactedVoxel.Dispose();
            vertPerCellIn.Dispose();
            Clean();

            return;
        }
        else if (isMeshEmpty == 3)
        {
            entity.values.isEmpty = true;
            entity.values.isFull = true;
            entity.values.isDoneLoading = true;

            vertPerCell.Dispose();
            compactedVoxel.Dispose();
            vertPerCellIn.Dispose();
            Clean();

            return;
        }
        else
        {
            entity.values.isEmpty = false;
            entity.values.isFull = false;
        }

        var countVJob = new MarchingCubeJobs.CountVertexPerVoxelJob()
        {
            densV = densities,
            nbTriTable = nbTriTable,
            triTable = triTable,
            vertPerCell = vertPerCellIn,
            chunkSize = baseChunkSize + 1,
            totalVoxel = totalSize,
            isoValue = isoValue
        };

        var countVJobHandle = countVJob.Schedule(totalSize, batchSize);
        countVJobHandle.Complete();


        //exclusivescan => compute the total number of vertices
        uint2 lastElem = vertPerCellIn[totalSize - 1];

        float timerEsc = Time.realtimeSinceStartup;

        var escanJob = new MarchingCubeJobs.ExclusiveScanTrivialJob()
        {
            vertPerCell = vertPerCellIn,
            result = vertPerCell,
            totalVoxel = totalSize
        };

        var escanJobJobHandle = escanJob.Schedule(countVJobHandle);
        escanJobJobHandle.Complete();


        uint2 lastScanElem = vertPerCell[totalSize - 1];

        uint newTotalVoxels = lastElem.y + lastScanElem.y;
        uint totalVerts = lastElem.x + lastScanElem.x;

        if (totalVerts <= 0)
        {
            //Debug.LogWarning("Empty iso-surface");
            entity.values.isDoneLoading = true;
            vertPerCell.Dispose();
            compactedVoxel.Dispose();
            vertPerCellIn.Dispose();
            Clean();
            return;
        }

        entity.values.drawBounds = true;

        curVertices = new NativeArray<float3>((int)totalVerts, Allocator.Persistent);
        curNormals = new NativeArray<float3>((int)totalVerts, Allocator.Persistent);
        //Double the triangles to have both faces
        curTriangles = new NativeArray<int>((int)totalVerts, Allocator.Persistent);

        //compactvoxels

        var compactJob = new MarchingCubeJobs.CompactVoxelJob()
        {
            vertPerCell = vertPerCell,
            compVoxel = compactedVoxel,
            chunkSize = baseChunkSize + 1,
            totalVoxel = totalSize,
            lastElem = lastElem.y
        };

        var compactJobHandle = compactJob.Schedule(totalSize, batchSize, escanJobJobHandle);
        compactJobHandle.Complete();


        //MC
        var MCJob = new MarchingCubeJobs.MarchingCubesJob()
        {
            vertices = curVertices,
            compVoxel = compactedVoxel,
            vertPerCell = vertPerCell,
            densV = densities,
            nbTriTable = nbTriTable,
            triTable = triTable,
            oriGrid = originGrid,
            dx = dx,
            chunkSize = baseChunkSize + 1,
            isoValue = isoValue,
            totalVerts = totalVerts,
            vertexScale = entity.values.vertexScale
        };
        var MCJobHandle = MCJob.Schedule((int)newTotalVoxels, batchSize, compactJobHandle);
        MCJobHandle.Complete();

        //Normals
        var NormJob = new MarchingCubeJobs.ComputeNormalsJob()
        {
            normals = curNormals,
            vertices = curVertices,
            densV = densities,
            oriGrid = originGrid,
            dx = dx,
            chunkSize = baseChunkSize + 1,
            vertexScale = entity.values.vertexScale
        };
        var NormJobHandle = NormJob.Schedule((int)totalVerts, batchSize, MCJobHandle);
        NormJobHandle.Complete();


        /*for (int i = 0; i < totalVerts - 3; i += 3) {
            curTriangles[i] = i;
            curTriangles[i + 1] = i + 1;
            curTriangles[i + 2] = i + 2;
        }
        //Double the triangles to have both faces
        for (int i = (int)totalVerts; i < totalVerts * 2 - 3; i += 3) {
            curTriangles[i] = i - (int)totalVerts;
            curTriangles[i + 2] = i + 1 - (int)totalVerts; //Invert triangles here
            curTriangles[i + 1] = i + 2 - (int)totalVerts;
        }*/

        /*for (int i = 0; i < totalVerts - 3; i += 3)
        {
            curTriangles[i] = i - (int)totalVerts;
            curTriangles[i + 2] = i + 1 - (int)totalVerts; //Invert triangles here
            curTriangles[i + 1] = i + 2 - (int)totalVerts;
        }*/

        vertPerCellIn.Dispose();
        vertPerCell.Dispose();
        compactedVoxel.Dispose();


        for (int i = 0; i < totalVerts; i += 3)
        {
            curTriangles[i + 2] = i;
            curTriangles[i + 1] = i + 1;
            curTriangles[i + 0] = i + 2;
        }

        Mesh mesh;

        entity.GetComponent<MeshCollider>().enabled = true;
        entity.GetComponent<MeshRenderer>().enabled = true;

        if (entity.GetComponent<MeshCollider>().sharedMesh != null)
            mesh = entity.GetComponent<MeshFilter>().sharedMesh;
        else
            mesh = new Mesh();

        mesh.Clear();
        mesh.SetVertices(curVertices);
        mesh.triangles = getTriangles();

        if(smoothNormals)
            mesh.SetNormals(curNormals);
        else
            mesh.RecalculateNormals();

        entity.GetComponent<MeshCollider>().sharedMesh = mesh;
        entity.GetComponent<MeshFilter>().mesh = mesh;

        if (entity.densities.Length == 0)
            entity.densities = new float[densities.Length];

        SetNativeDensityArray(entity.densities, densities);
        //entity.values.densities = new float[densities.Length];

        //SetNativeDensityArray(entity.values.densities, densities);

        //densities.CopyTo(entity.values.densities);

        //Debug.Log(entity.values.indexFromSplit);

        entity.values.isDoneLoading = true;

        //chunksGenerated++;

        Clean();
    }

    void SplitChunk(Chunk parentChunk, int index)
    {
        //logicUpdateList.RemoveAt(index);

        float splitTimer = Time.realtimeSinceStartup;

        //chunkMap.Remove(parentChunk.values.chunkPos);

        ShouldSplitLOD(parentChunk);

        /*if (loadQueue.Contains(parentChunk))
            loadQueue.Remove(parentChunk);*/

        byte childIndex = 0;

        for (int x = 0; x < 2; x++)
        {
            for (int y = 0; y < 2; y++)
            {
                for (int z = 0; z < 2; z++)
                {
                    GameObject tempChunk = GetChunk();
                    tempChunk.transform.SetParent(transform);

                    Chunk chunk = tempChunk.GetComponent<Chunk>();

                    chunk.values.lod = (byte)(parentChunk.values.lod - 1);
                    chunk.values.chunkSize = baseChunkSize;
                    chunk.values.actualChunkSize = baseChunkSize * (int)Mathf.Pow(2f, chunk.values.lod);
                    chunk.values.actualChunkSizeHalf = chunk.values.actualChunkSize / 2;
                    chunk.values.vertexScale = chunk.values.actualChunkSize / baseChunkSize;
                    chunk.values.wireColor = Color.red;
                    chunk.values.drawBounds = true;
                    chunk.values.isChild = true;

                    chunk.values.distanceFromPlayer = Mathf.Infinity;
                    chunk.planet = this;

                    chunk.parentFromSplit = parentChunk;
                    chunk.values.indexFromSplit = childIndex;

                    chunk.parentList = new int3[parentChunk.parentList.Length + 1];
                    parentChunk.parentList.CopyTo(chunk.parentList, 0);
                    chunk.parentList[chunk.parentList.Length - 1] = new int3(x, y, z);

                    tempChunk.transform.localPosition = new float3(x * chunk.values.actualChunkSize, y * chunk.values.actualChunkSize, z * chunk.values.actualChunkSize) + parentChunk.values.chunkPos;

                    chunk.values.chunkPos = new int3(x * chunk.values.actualChunkSize, y * chunk.values.actualChunkSize, z * chunk.values.actualChunkSize) + parentChunk.values.chunkPos;

                    /*chunkMap.Add(chunk.values.chunkPos, chunk);
                    logicUpdateList.Add(chunk);*/

                    loadQueue.Add(chunk);

                    parentChunk.childrenFromSplit[childIndex] = chunk;

                    childIndex++;
                }
            }
        }

        //Debug.Log("Split: " + (Time.realtimeSinceStartup - splitTimer) * 1000f + "ms");
    }

    static bool ShouldSplitDistance(float distance, int actualChunkSize, bool isEmpty, bool isFull, byte minLOD, byte lod)
    {
        bool result = false;

        float checkDist = (float)actualChunkSize / 2f;

        if (distance < (checkDist * checkDist) + 100f && (!isEmpty && !isFull) && lod > minLOD)
        {
            result = true;
        }

        return result;
    }

    static bool ShouldJoinDistance(float distance, int actualChunkSize)
    {
        bool join = false;

        float checkDist = (float)actualChunkSize * 8f + 400f;

        if (distance > (checkDist * checkDist))
        {
            join = true;
        }

        return join;
    }

    int3 parentPos;
    bool needToSplit = true;

    void ShouldSplitLOD(Chunk chunk)
    {
        if (chunk.values.lod < maxLOD - 1)
        {
            parentPos = chunk.values.chunkPos - chunk.parentList[chunk.parentList.Length - 1] * chunk.values.actualChunkSize;

            int3[] sides = GetAdjacentSides(chunk.values.chunkPos, chunk.values.actualChunkSize, chunk.parentList[chunk.parentList.Length - 1]);
            //Vector3Int[] parentSides = GetAdjacentSidesParent(chunk.chunkPos, chunk.actualChunkSize * 2f, chunk.parentList[chunk.parentList.Count - 2]);

            for (int i = 0; i < sides.Length; i++)
            {
                needToSplit = true;

                if (chunkMap.ContainsKey(sides[i]) && chunkMap[sides[i]].values.lod <= chunk.values.lod)
                {
                    needToSplit = false;
                    //Debug.Log("DONT SPLIT");
                }
                else
                {
                    //Debug.Log("WHY SPLIT");
                }

                if (needToSplit && IsInBounds(GetParentChunk(sides[i], chunk.values.actualChunkSize * 2), LODActualSize) && chunkMap.ContainsKey(GetParentChunk(sides[i], chunk.values.actualChunkSize * 2)))
                {
                    //Debug.Log("SPLIT LOD: " + GetParentChunk(sides[i], chunk.actualChunkSize * 4f));



                    chunkMap[GetParentChunk(sides[i], chunk.values.actualChunkSize * 2)].values.markedToSplit = true;
                    //Debug.Log("SPLIT");

                    /*if (!chunk.hasJoined && chunk.doneLoading && !chunk.isEmptyOrFull && !chunk.hasSplit && ((chunk.isChild && chunk.parentChunkFromSplit == null) || !chunk.isChild))
                    {
                        if (chunk.markedToSplit)
                        {
                            //Debug.Log("SPLIT");
                            SplitChunk(chunk, true);
                        }
                    }*/



                    //SplitChunk(chunkMap[GetParentChunk(sides[i], chunk.actualChunkSize * 4f)], true);
                }
            }
        }
    }

    List<Chunk> chunkSiblings = new List<Chunk>(8);
    //List<Chunk> chunkObjects = new List<Chunk>(8);

    public void JoinChunks(Chunk mainSibling)
    {
        chunkSiblings.Clear();
        //chunkObjects.Clear();

        for (int x = 0; x < 2; x++)
        {
            for (int y = 0; y < 2; y++)
            {
                for (int z = 0; z < 2; z++)
                {
                    chunkSiblings.Add(chunkMap[new int3(x, y, z) * mainSibling.values.actualChunkSize + mainSibling.values.chunkPos]);
                    //chunkObjects.Add(chunkSiblings[chunkSiblings.Count - 1]);
                }
            }
        }

        for (int i = 0; i < 8; i++)
        {
            //chunkSiblings[i].GetComponent<MeshRenderer>().enabled = false;
            //chunkSiblings[i].GetComponent<MeshCollider>().enabled = false;
            chunkSiblings[i].values.drawBounds = false;

            //chunkSiblings[i].values.hasJoined = true;

            chunkMap.Remove(chunkSiblings[i].values.chunkPos);

            logicUpdateList.Remove(chunkSiblings[i]);

            //RemoveChunk(chunkSiblings[i]);

            chunksGenerated--;

            loadQueue.Remove(chunkSiblings[i]);
            //chunksLoadQueue.Remove(chunkSiblings[i]);
            //chunksToUpdate.Remove(chunkSiblings[i]);
        }


        GameObject tempChunk = GetChunk();
        Chunk chunk = tempChunk.GetComponent<Chunk>();

        float3 chunkPos = new float3(mainSibling.values.chunkPos);


        tempChunk.transform.SetParent(transform);

        chunk.values.lod = (byte)(mainSibling.values.lod + 1);
        chunk.values.chunkSize = baseChunkSize;
        chunk.values.actualChunkSize = baseChunkSize * (int)Mathf.Pow(2f, chunk.values.lod);
        chunk.values.actualChunkSizeHalf = chunk.values.actualChunkSize / 2;
        chunk.values.vertexScale = chunk.values.actualChunkSize / baseChunkSize;
        chunk.values.isChild = false;
        chunk.values.wireColor = Color.yellow;
        chunk.values.distanceFromPlayer = Mathf.Infinity;
        chunk.planet = this;

        //chunk.parentFromSplit = parentChunk;
        //chunk.values.indexFromSplit = childIndex;

        chunk.parentList = new int3[mainSibling.parentList.Length - 1];
        System.Array.Copy(mainSibling.parentList, 0, chunk.parentList, 0, mainSibling.parentList.Length - 1);
        //mainSibling.parentList.CopyTo(chunk.parentList, 0);

        tempChunk.transform.localPosition = chunkPos;

        chunk.values.chunkPos = mainSibling.values.chunkPos;

        chunkMap.Add(chunk.values.chunkPos, chunk);
        loadQueue.Add(chunk);
        logicUpdateList.Add(chunk);




        //chunk.isChild = false;
        //chunk.chunkColor = Color.yellow;

        //chunk.Init();

        //chunk.GetComponent<MeshRenderer>().enabled = true;
        //chunk.GetComponent<MeshCollider>().enabled = true;

        //if (parentChunk.markedToSplit)
        //    chunk.chunkColor = Color.green;
        //chunkObjects.CopyTo(chunk.childrenFromJoin, 0);
        Chunk[] temp = (Chunk[])chunkSiblings.ToArray().Clone();
        chunk.childrenFromJoin = temp.ToList();

        chunk.values.hasJoined = true;

        //RemoveChunk(mainSibling);

        chunksGenerated--;

        for (int i = 0; i < 8; i++)
        {
            //RemoveChunk(chunkSiblings[i]);
            //chunksLoadQueue.Remove(chunkSiblings[i]);
            //chunksToUpdate.Remove(chunkSiblings[i]);
        }

        //chunkMap.Add(chunk.values.chunkPos, chunk);
        //chunksToUpdate.Add(chunk);
        //logicUpdateList.Add(chunk);

        //chunksLoadQueue.Add(chunk);
        //loadQueue.Add(chunk);

        //RemoveChunk(mainSibling);
    }

    float posModifier;
    float scaleFactor;

    bool isFull;
    bool isEmpty;
    float thisHeight;

    byte emptyReturnValue;

    byte GenerateTerrainData(Chunk entity)
    {
        emptyReturnValue = 0;
        isFull = true;
        isEmpty = true;

        scaleFactor = entity.values.actualChunkSize / baseChunkSize;
        densities = new NativeArray<float>(totalSize, Allocator.Persistent);
        posModifier = (LODActualSize / 2f);

        noise1.fastNoiseSIMD.SetAxisScales(scaleFactor, scaleFactor, scaleFactor);
        generationData = noise1.fastNoiseSIMD.GetNoiseSet(entity.values.chunkPos.x / (int)scaleFactor, entity.values.chunkPos.y / (int)scaleFactor, entity.values.chunkPos.z / (int)scaleFactor, baseChunkSize + 1, baseChunkSize + 1, baseChunkSize + 1);

        noise2.fastNoiseSIMD.SetAxisScales(scaleFactor, scaleFactor, scaleFactor);
        generationData2 = noise2.fastNoiseSIMD.GetNoiseSet(entity.values.chunkPos.x / (int)scaleFactor, entity.values.chunkPos.y / (int)scaleFactor, entity.values.chunkPos.z / (int)scaleFactor, baseChunkSize + 1, baseChunkSize + 1, baseChunkSize + 1);

        int id = 0;
        for (int i = 0; i < baseChunkSize + 1; i++)
        {
            for (int j = 0; j < baseChunkSize + 1; j++)
            {
                for (int k = 0; k < baseChunkSize + 1; k++)
                {
                    //float x = i - posModifier + entity.chunkPos.x / scaleFactor;
                    //float y = j - posModifier + entity.chunkPos.y / scaleFactor;
                    //float z = k - posModifier + entity.chunkPos.z / scaleFactor;
                    float x = i * scaleFactor - posModifier + entity.values.chunkPos.x;
                    float y = j * scaleFactor - posModifier + entity.values.chunkPos.y;
                    float z = k * scaleFactor - posModifier + entity.values.chunkPos.z;

                    //densVal[id++] = (x * x * x * x - 5.0f * x * x + y * y * y * y - 5.0f * y * y + z * z * z * z - 5.0f * z * z + 11.8f) * 0.2f + 0.5f;

                    //float thisHeight = 8f * Mathf.PerlinNoise((i + tempOffset.x) * .1f, (k + tempOffset.y) * .1f);
                    thisHeight = (x * x + y * y + z * z) - (radius * radius) + (Mathf.Min(0, generationData[id] * 10000000000f - 1f)) + generationData2[id] * 6000000f;

                    densities[id] = thisHeight;
                    id++;

                    if (thisHeight < isoValue)
                        isEmpty = false;

                    if (thisHeight > isoValue)
                        isFull = false;

                    // densities[id++] = j - thisHeight;
                }
            }
        }

        if (isEmpty && !isFull)
        {
            emptyReturnValue = 1;
        }
        else if (isEmpty && isFull)
        {
            isEmpty = true;
            isFull = true;
            emptyReturnValue = 3;
        }
        else
            isFull = false;

        return emptyReturnValue;
    }

    public int[] getTriangles()
    {
        int[] res = new int[curTriangles.Length];
        SetNativeTriangleArray(res, curTriangles);
        return res;
    }

    //From https://gist.github.com/LotteMakesStuff/c2f9b764b15f74d14c00ceb4214356b4
    unsafe void SetNativeTriangleArray(int[] triArray, NativeArray<int> triBuffer)
    {
        // pin the target vertex array and get a pointer to it
        fixed (void* triArrayPointer = triArray)
        {
            // memcopy the native array over the top
            UnsafeUtility.MemCpy(triArrayPointer, NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(triBuffer), triArray.Length * (long)UnsafeUtility.SizeOf<int>());
        }
    }

    unsafe void SetNativeDensityArray(float[] triArray, NativeArray<float> triBuffer)
    {
        // pin the target vertex array and get a pointer to it
        fixed (void* triArrayPointer = triArray)
        {
            // memcopy the native array over the top
            UnsafeUtility.MemCpy(triArrayPointer, NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(triBuffer), triArray.Length * (long)UnsafeUtility.SizeOf<float>());
        }
    }

    GameObject GetChunk()
    {
        GameObject chunk;

        if (chunkPool.Count == 0)
        {
            chunk = Instantiate(chunkPrefab);

            //chunk.GetComponent<Chunk>().enabled = false;
            chunk.transform.SetParent(transform);

            chunk.GetComponent<Chunk>().values.isDoneLoading = false;
            chunk.GetComponent<Chunk>().values.wireColor = Color.red;

            Mesh newMesh = new Mesh();
            newMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            chunk.GetComponent<MeshFilter>().mesh = newMesh;
            chunk.GetComponent<MeshCollider>().sharedMesh = newMesh;

            //Debug.Log("NEW CHUNK");
        }
        else
            chunk = chunkPool.Dequeue();

        chunk.GetComponent<Chunk>().enabled = true;
        chunk.GetComponent<Chunk>().values.isDoneLoading = false;
        chunk.GetComponent<Chunk>().values.hasJoined = false;
        chunk.GetComponent<Chunk>().values.hasSplit = false;

        chunk.GetComponent<Chunk>().values.lod = 0;
        chunk.GetComponent<Chunk>().values.chunkSize = 0;
        chunk.GetComponent<Chunk>().values.actualChunkSize = 0;
        chunk.GetComponent<Chunk>().values.actualChunkSizeHalf = 0;
        chunk.GetComponent<Chunk>().values.chunkPos = 0;
        chunk.GetComponent<Chunk>().values.distanceFromPlayer = 0;
        chunk.GetComponent<Chunk>().values.drawBounds = true;
        chunk.GetComponent<Chunk>().values.vertexScale = 0;
        chunk.GetComponent<Chunk>().values.isEmpty = false;
        chunk.GetComponent<Chunk>().values.isFull = false;
        chunk.GetComponent<Chunk>().values.markedToSplit = false;
        chunk.GetComponent<Chunk>().values.isChild = false;
        chunk.GetComponent<Chunk>().values.indexForUpdate = 0;
        chunk.GetComponent<Chunk>().values.indexFromSplit = 0;

        chunk.GetComponent<MeshRenderer>().enabled = false;
        chunk.GetComponent<MeshCollider>().enabled = false;

        return chunk;
    }

    void RemoveChunk(Chunk chunk)
    {
        chunk.GetComponent<MeshFilter>().mesh.Clear();
        chunk.GetComponent<MeshCollider>().sharedMesh.Clear();

        chunkPool.Enqueue(chunk.gameObject);
        chunk.enabled = false;
    }

    static int3[] GetAdjacentSides(int3 chunkPos, int realChunkSize, int3 localPos)
    {
        int3[] result = new int3[3];

        int x = (int)(localPos.x * 2f) - 1;
        int y = (int)(localPos.y * 2f) - 1;
        int z = (int)(localPos.z * 2f) - 1;

        result[0] = new int3(chunkPos.x + (x * realChunkSize), chunkPos.y, chunkPos.z);
        result[1] = new int3(chunkPos.x, chunkPos.x + (y * realChunkSize), chunkPos.z);
        result[2] = new int3(chunkPos.x, chunkPos.y, chunkPos.z + (z * realChunkSize));

        return result;
    }

    static int3 GetParentChunk(float3 chunkPos, int realChunkSize)
    {
        return new int3(Mathf.FloorToInt(chunkPos.x / realChunkSize) * realChunkSize, Mathf.FloorToInt(chunkPos.y / realChunkSize) * realChunkSize, Mathf.FloorToInt(chunkPos.z / realChunkSize) * realChunkSize);
    }

    static bool IsInBounds(int3 pos, int LODActualSize)
    {
        if (pos.x < LODActualSize && pos.x > 0f && pos.y < LODActualSize && pos.y > 0f && pos.z < LODActualSize && pos.z > 0f)
        {
            return true;
        }
        else
            return false;
    }

    public void Clean()
    {

        if (densities.IsCreated)
            densities.Dispose();

        /*
        if (nbTriTable.IsCreated)
            nbTriTable.Dispose();
        if (triTable.IsCreated)
            triTable.Dispose();
            */

        if (curVertices.IsCreated)
            curVertices.Dispose();
        if (curNormals.IsCreated)
            curNormals.Dispose();
        if (curTriangles.IsCreated)
            curTriangles.Dispose();
    }

    void initTriTable()
    {
        nbTriTable = new NativeArray<int>(256, Allocator.Persistent);
        triTable = new NativeArray<int>(4096, Allocator.Persistent);
        int id = 0;
        for (int i = 0; i < managed_triTable.GetLength(0); i++)
        {
            for (int j = 0; j < managed_triTable.GetLength(1); j++)
            {
                triTable[id++] = managed_triTable[i, j];
            }
        }
        for (int i = 0; i < managed_nbTriTable.Length; i++)
        {
            nbTriTable[i] = managed_nbTriTable[i];
        }
    }

    #region Triangulation Tables
    public static int[,] managed_triTable = new int[,]
    {
        {-1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {0, 8, 3, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {0, 1, 9, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {1, 8, 3, 9, 8, 1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {1, 2, 10, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {0, 8, 3, 1, 2, 10, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {9, 2, 10, 0, 2, 9, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {2, 8, 3, 2, 10, 8, 10, 9, 8, -1, -1, -1, -1, -1, -1, -1},
        {3, 11, 2, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {0, 11, 2, 8, 11, 0, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {1, 9, 0, 2, 3, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {1, 11, 2, 1, 9, 11, 9, 8, 11, -1, -1, -1, -1, -1, -1, -1},
        {3, 10, 1, 11, 10, 3, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {0, 10, 1, 0, 8, 10, 8, 11, 10, -1, -1, -1, -1, -1, -1, -1},
        {3, 9, 0, 3, 11, 9, 11, 10, 9, -1, -1, -1, -1, -1, -1, -1},
        {9, 8, 10, 10, 8, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {4, 7, 8, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {4, 3, 0, 7, 3, 4, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {0, 1, 9, 8, 4, 7, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {4, 1, 9, 4, 7, 1, 7, 3, 1, -1, -1, -1, -1, -1, -1, -1},
        {1, 2, 10, 8, 4, 7, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {3, 4, 7, 3, 0, 4, 1, 2, 10, -1, -1, -1, -1, -1, -1, -1},
        {9, 2, 10, 9, 0, 2, 8, 4, 7, -1, -1, -1, -1, -1, -1, -1},
        {2, 10, 9, 2, 9, 7, 2, 7, 3, 7, 9, 4, -1, -1, -1, -1},
        {8, 4, 7, 3, 11, 2, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {11, 4, 7, 11, 2, 4, 2, 0, 4, -1, -1, -1, -1, -1, -1, -1},
        {9, 0, 1, 8, 4, 7, 2, 3, 11, -1, -1, -1, -1, -1, -1, -1},
        {4, 7, 11, 9, 4, 11, 9, 11, 2, 9, 2, 1, -1, -1, -1, -1},
        {3, 10, 1, 3, 11, 10, 7, 8, 4, -1, -1, -1, -1, -1, -1, -1},
        {1, 11, 10, 1, 4, 11, 1, 0, 4, 7, 11, 4, -1, -1, -1, -1},
        {4, 7, 8, 9, 0, 11, 9, 11, 10, 11, 0, 3, -1, -1, -1, -1},
        {4, 7, 11, 4, 11, 9, 9, 11, 10, -1, -1, -1, -1, -1, -1, -1},
        {9, 5, 4, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {9, 5, 4, 0, 8, 3, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {0, 5, 4, 1, 5, 0, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {8, 5, 4, 8, 3, 5, 3, 1, 5, -1, -1, -1, -1, -1, -1, -1},
        {1, 2, 10, 9, 5, 4, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {3, 0, 8, 1, 2, 10, 4, 9, 5, -1, -1, -1, -1, -1, -1, -1},
        {5, 2, 10, 5, 4, 2, 4, 0, 2, -1, -1, -1, -1, -1, -1, -1},
        {2, 10, 5, 3, 2, 5, 3, 5, 4, 3, 4, 8, -1, -1, -1, -1},
        {9, 5, 4, 2, 3, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {0, 11, 2, 0, 8, 11, 4, 9, 5, -1, -1, -1, -1, -1, -1, -1},
        {0, 5, 4, 0, 1, 5, 2, 3, 11, -1, -1, -1, -1, -1, -1, -1},
        {2, 1, 5, 2, 5, 8, 2, 8, 11, 4, 8, 5, -1, -1, -1, -1},
        {10, 3, 11, 10, 1, 3, 9, 5, 4, -1, -1, -1, -1, -1, -1, -1},
        {4, 9, 5, 0, 8, 1, 8, 10, 1, 8, 11, 10, -1, -1, -1, -1},
        {5, 4, 0, 5, 0, 11, 5, 11, 10, 11, 0, 3, -1, -1, -1, -1},
        {5, 4, 8, 5, 8, 10, 10, 8, 11, -1, -1, -1, -1, -1, -1, -1},
        {9, 7, 8, 5, 7, 9, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {9, 3, 0, 9, 5, 3, 5, 7, 3, -1, -1, -1, -1, -1, -1, -1},
        {0, 7, 8, 0, 1, 7, 1, 5, 7, -1, -1, -1, -1, -1, -1, -1},
        {1, 5, 3, 3, 5, 7, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {9, 7, 8, 9, 5, 7, 10, 1, 2, -1, -1, -1, -1, -1, -1, -1},
        {10, 1, 2, 9, 5, 0, 5, 3, 0, 5, 7, 3, -1, -1, -1, -1},
        {8, 0, 2, 8, 2, 5, 8, 5, 7, 10, 5, 2, -1, -1, -1, -1},
        {2, 10, 5, 2, 5, 3, 3, 5, 7, -1, -1, -1, -1, -1, -1, -1},
        {7, 9, 5, 7, 8, 9, 3, 11, 2, -1, -1, -1, -1, -1, -1, -1},
        {9, 5, 7, 9, 7, 2, 9, 2, 0, 2, 7, 11, -1, -1, -1, -1},
        {2, 3, 11, 0, 1, 8, 1, 7, 8, 1, 5, 7, -1, -1, -1, -1},
        {11, 2, 1, 11, 1, 7, 7, 1, 5, -1, -1, -1, -1, -1, -1, -1},
        {9, 5, 8, 8, 5, 7, 10, 1, 3, 10, 3, 11, -1, -1, -1, -1},
        {5, 7, 0, 5, 0, 9, 7, 11, 0, 1, 0, 10, 11, 10, 0, -1},
        {11, 10, 0, 11, 0, 3, 10, 5, 0, 8, 0, 7, 5, 7, 0, -1},
        {11, 10, 5, 7, 11, 5, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {10, 6, 5, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {0, 8, 3, 5, 10, 6, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {9, 0, 1, 5, 10, 6, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {1, 8, 3, 1, 9, 8, 5, 10, 6, -1, -1, -1, -1, -1, -1, -1},
        {1, 6, 5, 2, 6, 1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {1, 6, 5, 1, 2, 6, 3, 0, 8, -1, -1, -1, -1, -1, -1, -1},
        {9, 6, 5, 9, 0, 6, 0, 2, 6, -1, -1, -1, -1, -1, -1, -1},
        {5, 9, 8, 5, 8, 2, 5, 2, 6, 3, 2, 8, -1, -1, -1, -1},
        {2, 3, 11, 10, 6, 5, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {11, 0, 8, 11, 2, 0, 10, 6, 5, -1, -1, -1, -1, -1, -1, -1},
        {0, 1, 9, 2, 3, 11, 5, 10, 6, -1, -1, -1, -1, -1, -1, -1},
        {5, 10, 6, 1, 9, 2, 9, 11, 2, 9, 8, 11, -1, -1, -1, -1},
        {6, 3, 11, 6, 5, 3, 5, 1, 3, -1, -1, -1, -1, -1, -1, -1},
        {0, 8, 11, 0, 11, 5, 0, 5, 1, 5, 11, 6, -1, -1, -1, -1},
        {3, 11, 6, 0, 3, 6, 0, 6, 5, 0, 5, 9, -1, -1, -1, -1},
        {6, 5, 9, 6, 9, 11, 11, 9, 8, -1, -1, -1, -1, -1, -1, -1},
        {5, 10, 6, 4, 7, 8, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {4, 3, 0, 4, 7, 3, 6, 5, 10, -1, -1, -1, -1, -1, -1, -1},
        {1, 9, 0, 5, 10, 6, 8, 4, 7, -1, -1, -1, -1, -1, -1, -1},
        {10, 6, 5, 1, 9, 7, 1, 7, 3, 7, 9, 4, -1, -1, -1, -1},
        {6, 1, 2, 6, 5, 1, 4, 7, 8, -1, -1, -1, -1, -1, -1, -1},
        {1, 2, 5, 5, 2, 6, 3, 0, 4, 3, 4, 7, -1, -1, -1, -1},
        {8, 4, 7, 9, 0, 5, 0, 6, 5, 0, 2, 6, -1, -1, -1, -1},
        {7, 3, 9, 7, 9, 4, 3, 2, 9, 5, 9, 6, 2, 6, 9, -1},
        {3, 11, 2, 7, 8, 4, 10, 6, 5, -1, -1, -1, -1, -1, -1, -1},
        {5, 10, 6, 4, 7, 2, 4, 2, 0, 2, 7, 11, -1, -1, -1, -1},
        {0, 1, 9, 4, 7, 8, 2, 3, 11, 5, 10, 6, -1, -1, -1, -1},
        {9, 2, 1, 9, 11, 2, 9, 4, 11, 7, 11, 4, 5, 10, 6, -1},
        {8, 4, 7, 3, 11, 5, 3, 5, 1, 5, 11, 6, -1, -1, -1, -1},
        {5, 1, 11, 5, 11, 6, 1, 0, 11, 7, 11, 4, 0, 4, 11, -1},
        {0, 5, 9, 0, 6, 5, 0, 3, 6, 11, 6, 3, 8, 4, 7, -1},
        {6, 5, 9, 6, 9, 11, 4, 7, 9, 7, 11, 9, -1, -1, -1, -1},
        {10, 4, 9, 6, 4, 10, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {4, 10, 6, 4, 9, 10, 0, 8, 3, -1, -1, -1, -1, -1, -1, -1},
        {10, 0, 1, 10, 6, 0, 6, 4, 0, -1, -1, -1, -1, -1, -1, -1},
        {8, 3, 1, 8, 1, 6, 8, 6, 4, 6, 1, 10, -1, -1, -1, -1},
        {1, 4, 9, 1, 2, 4, 2, 6, 4, -1, -1, -1, -1, -1, -1, -1},
        {3, 0, 8, 1, 2, 9, 2, 4, 9, 2, 6, 4, -1, -1, -1, -1},
        {0, 2, 4, 4, 2, 6, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {8, 3, 2, 8, 2, 4, 4, 2, 6, -1, -1, -1, -1, -1, -1, -1},
        {10, 4, 9, 10, 6, 4, 11, 2, 3, -1, -1, -1, -1, -1, -1, -1},
        {0, 8, 2, 2, 8, 11, 4, 9, 10, 4, 10, 6, -1, -1, -1, -1},
        {3, 11, 2, 0, 1, 6, 0, 6, 4, 6, 1, 10, -1, -1, -1, -1},
        {6, 4, 1, 6, 1, 10, 4, 8, 1, 2, 1, 11, 8, 11, 1, -1},
        {9, 6, 4, 9, 3, 6, 9, 1, 3, 11, 6, 3, -1, -1, -1, -1},
        {8, 11, 1, 8, 1, 0, 11, 6, 1, 9, 1, 4, 6, 4, 1, -1},
        {3, 11, 6, 3, 6, 0, 0, 6, 4, -1, -1, -1, -1, -1, -1, -1},
        {6, 4, 8, 11, 6, 8, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {7, 10, 6, 7, 8, 10, 8, 9, 10, -1, -1, -1, -1, -1, -1, -1},
        {0, 7, 3, 0, 10, 7, 0, 9, 10, 6, 7, 10, -1, -1, -1, -1},
        {10, 6, 7, 1, 10, 7, 1, 7, 8, 1, 8, 0, -1, -1, -1, -1},
        {10, 6, 7, 10, 7, 1, 1, 7, 3, -1, -1, -1, -1, -1, -1, -1},
        {1, 2, 6, 1, 6, 8, 1, 8, 9, 8, 6, 7, -1, -1, -1, -1},
        {2, 6, 9, 2, 9, 1, 6, 7, 9, 0, 9, 3, 7, 3, 9, -1},
        {7, 8, 0, 7, 0, 6, 6, 0, 2, -1, -1, -1, -1, -1, -1, -1},
        {7, 3, 2, 6, 7, 2, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {2, 3, 11, 10, 6, 8, 10, 8, 9, 8, 6, 7, -1, -1, -1, -1},
        {2, 0, 7, 2, 7, 11, 0, 9, 7, 6, 7, 10, 9, 10, 7, -1},
        {1, 8, 0, 1, 7, 8, 1, 10, 7, 6, 7, 10, 2, 3, 11, -1},
        {11, 2, 1, 11, 1, 7, 10, 6, 1, 6, 7, 1, -1, -1, -1, -1},
        {8, 9, 6, 8, 6, 7, 9, 1, 6, 11, 6, 3, 1, 3, 6, -1},
        {0, 9, 1, 11, 6, 7, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {7, 8, 0, 7, 0, 6, 3, 11, 0, 11, 6, 0, -1, -1, -1, -1},
        {7, 11, 6, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {7, 6, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {3, 0, 8, 11, 7, 6, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {0, 1, 9, 11, 7, 6, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {8, 1, 9, 8, 3, 1, 11, 7, 6, -1, -1, -1, -1, -1, -1, -1},
        {10, 1, 2, 6, 11, 7, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {1, 2, 10, 3, 0, 8, 6, 11, 7, -1, -1, -1, -1, -1, -1, -1},
        {2, 9, 0, 2, 10, 9, 6, 11, 7, -1, -1, -1, -1, -1, -1, -1},
        {6, 11, 7, 2, 10, 3, 10, 8, 3, 10, 9, 8, -1, -1, -1, -1},
        {7, 2, 3, 6, 2, 7, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {7, 0, 8, 7, 6, 0, 6, 2, 0, -1, -1, -1, -1, -1, -1, -1},
        {2, 7, 6, 2, 3, 7, 0, 1, 9, -1, -1, -1, -1, -1, -1, -1},
        {1, 6, 2, 1, 8, 6, 1, 9, 8, 8, 7, 6, -1, -1, -1, -1},
        {10, 7, 6, 10, 1, 7, 1, 3, 7, -1, -1, -1, -1, -1, -1, -1},
        {10, 7, 6, 1, 7, 10, 1, 8, 7, 1, 0, 8, -1, -1, -1, -1},
        {0, 3, 7, 0, 7, 10, 0, 10, 9, 6, 10, 7, -1, -1, -1, -1},
        {7, 6, 10, 7, 10, 8, 8, 10, 9, -1, -1, -1, -1, -1, -1, -1},
        {6, 8, 4, 11, 8, 6, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {3, 6, 11, 3, 0, 6, 0, 4, 6, -1, -1, -1, -1, -1, -1, -1},
        {8, 6, 11, 8, 4, 6, 9, 0, 1, -1, -1, -1, -1, -1, -1, -1},
        {9, 4, 6, 9, 6, 3, 9, 3, 1, 11, 3, 6, -1, -1, -1, -1},
        {6, 8, 4, 6, 11, 8, 2, 10, 1, -1, -1, -1, -1, -1, -1, -1},
        {1, 2, 10, 3, 0, 11, 0, 6, 11, 0, 4, 6, -1, -1, -1, -1},
        {4, 11, 8, 4, 6, 11, 0, 2, 9, 2, 10, 9, -1, -1, -1, -1},
        {10, 9, 3, 10, 3, 2, 9, 4, 3, 11, 3, 6, 4, 6, 3, -1},
        {8, 2, 3, 8, 4, 2, 4, 6, 2, -1, -1, -1, -1, -1, -1, -1},
        {0, 4, 2, 4, 6, 2, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {1, 9, 0, 2, 3, 4, 2, 4, 6, 4, 3, 8, -1, -1, -1, -1},
        {1, 9, 4, 1, 4, 2, 2, 4, 6, -1, -1, -1, -1, -1, -1, -1},
        {8, 1, 3, 8, 6, 1, 8, 4, 6, 6, 10, 1, -1, -1, -1, -1},
        {10, 1, 0, 10, 0, 6, 6, 0, 4, -1, -1, -1, -1, -1, -1, -1},
        {4, 6, 3, 4, 3, 8, 6, 10, 3, 0, 3, 9, 10, 9, 3, -1},
        {10, 9, 4, 6, 10, 4, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {4, 9, 5, 7, 6, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {0, 8, 3, 4, 9, 5, 11, 7, 6, -1, -1, -1, -1, -1, -1, -1},
        {5, 0, 1, 5, 4, 0, 7, 6, 11, -1, -1, -1, -1, -1, -1, -1},
        {11, 7, 6, 8, 3, 4, 3, 5, 4, 3, 1, 5, -1, -1, -1, -1},
        {9, 5, 4, 10, 1, 2, 7, 6, 11, -1, -1, -1, -1, -1, -1, -1},
        {6, 11, 7, 1, 2, 10, 0, 8, 3, 4, 9, 5, -1, -1, -1, -1},
        {7, 6, 11, 5, 4, 10, 4, 2, 10, 4, 0, 2, -1, -1, -1, -1},
        {3, 4, 8, 3, 5, 4, 3, 2, 5, 10, 5, 2, 11, 7, 6, -1},
        {7, 2, 3, 7, 6, 2, 5, 4, 9, -1, -1, -1, -1, -1, -1, -1},
        {9, 5, 4, 0, 8, 6, 0, 6, 2, 6, 8, 7, -1, -1, -1, -1},
        {3, 6, 2, 3, 7, 6, 1, 5, 0, 5, 4, 0, -1, -1, -1, -1},
        {6, 2, 8, 6, 8, 7, 2, 1, 8, 4, 8, 5, 1, 5, 8, -1},
        {9, 5, 4, 10, 1, 6, 1, 7, 6, 1, 3, 7, -1, -1, -1, -1},
        {1, 6, 10, 1, 7, 6, 1, 0, 7, 8, 7, 0, 9, 5, 4, -1},
        {4, 0, 10, 4, 10, 5, 0, 3, 10, 6, 10, 7, 3, 7, 10, -1},
        {7, 6, 10, 7, 10, 8, 5, 4, 10, 4, 8, 10, -1, -1, -1, -1},
        {6, 9, 5, 6, 11, 9, 11, 8, 9, -1, -1, -1, -1, -1, -1, -1},
        {3, 6, 11, 0, 6, 3, 0, 5, 6, 0, 9, 5, -1, -1, -1, -1},
        {0, 11, 8, 0, 5, 11, 0, 1, 5, 5, 6, 11, -1, -1, -1, -1},
        {6, 11, 3, 6, 3, 5, 5, 3, 1, -1, -1, -1, -1, -1, -1, -1},
        {1, 2, 10, 9, 5, 11, 9, 11, 8, 11, 5, 6, -1, -1, -1, -1},
        {0, 11, 3, 0, 6, 11, 0, 9, 6, 5, 6, 9, 1, 2, 10, -1},
        {11, 8, 5, 11, 5, 6, 8, 0, 5, 10, 5, 2, 0, 2, 5, -1},
        {6, 11, 3, 6, 3, 5, 2, 10, 3, 10, 5, 3, -1, -1, -1, -1},
        {5, 8, 9, 5, 2, 8, 5, 6, 2, 3, 8, 2, -1, -1, -1, -1},
        {9, 5, 6, 9, 6, 0, 0, 6, 2, -1, -1, -1, -1, -1, -1, -1},
        {1, 5, 8, 1, 8, 0, 5, 6, 8, 3, 8, 2, 6, 2, 8, -1},
        {1, 5, 6, 2, 1, 6, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {1, 3, 6, 1, 6, 10, 3, 8, 6, 5, 6, 9, 8, 9, 6, -1},
        {10, 1, 0, 10, 0, 6, 9, 5, 0, 5, 6, 0, -1, -1, -1, -1},
        {0, 3, 8, 5, 6, 10, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {10, 5, 6, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {11, 5, 10, 7, 5, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {11, 5, 10, 11, 7, 5, 8, 3, 0, -1, -1, -1, -1, -1, -1, -1},
        {5, 11, 7, 5, 10, 11, 1, 9, 0, -1, -1, -1, -1, -1, -1, -1},
        {10, 7, 5, 10, 11, 7, 9, 8, 1, 8, 3, 1, -1, -1, -1, -1},
        {11, 1, 2, 11, 7, 1, 7, 5, 1, -1, -1, -1, -1, -1, -1, -1},
        {0, 8, 3, 1, 2, 7, 1, 7, 5, 7, 2, 11, -1, -1, -1, -1},
        {9, 7, 5, 9, 2, 7, 9, 0, 2, 2, 11, 7, -1, -1, -1, -1},
        {7, 5, 2, 7, 2, 11, 5, 9, 2, 3, 2, 8, 9, 8, 2, -1},
        {2, 5, 10, 2, 3, 5, 3, 7, 5, -1, -1, -1, -1, -1, -1, -1},
        {8, 2, 0, 8, 5, 2, 8, 7, 5, 10, 2, 5, -1, -1, -1, -1},
        {9, 0, 1, 5, 10, 3, 5, 3, 7, 3, 10, 2, -1, -1, -1, -1},
        {9, 8, 2, 9, 2, 1, 8, 7, 2, 10, 2, 5, 7, 5, 2, -1},
        {1, 3, 5, 3, 7, 5, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {0, 8, 7, 0, 7, 1, 1, 7, 5, -1, -1, -1, -1, -1, -1, -1},
        {9, 0, 3, 9, 3, 5, 5, 3, 7, -1, -1, -1, -1, -1, -1, -1},
        {9, 8, 7, 5, 9, 7, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {5, 8, 4, 5, 10, 8, 10, 11, 8, -1, -1, -1, -1, -1, -1, -1},
        {5, 0, 4, 5, 11, 0, 5, 10, 11, 11, 3, 0, -1, -1, -1, -1},
        {0, 1, 9, 8, 4, 10, 8, 10, 11, 10, 4, 5, -1, -1, -1, -1},
        {10, 11, 4, 10, 4, 5, 11, 3, 4, 9, 4, 1, 3, 1, 4, -1},
        {2, 5, 1, 2, 8, 5, 2, 11, 8, 4, 5, 8, -1, -1, -1, -1},
        {0, 4, 11, 0, 11, 3, 4, 5, 11, 2, 11, 1, 5, 1, 11, -1},
        {0, 2, 5, 0, 5, 9, 2, 11, 5, 4, 5, 8, 11, 8, 5, -1},
        {9, 4, 5, 2, 11, 3, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {2, 5, 10, 3, 5, 2, 3, 4, 5, 3, 8, 4, -1, -1, -1, -1},
        {5, 10, 2, 5, 2, 4, 4, 2, 0, -1, -1, -1, -1, -1, -1, -1},
        {3, 10, 2, 3, 5, 10, 3, 8, 5, 4, 5, 8, 0, 1, 9, -1},
        {5, 10, 2, 5, 2, 4, 1, 9, 2, 9, 4, 2, -1, -1, -1, -1},
        {8, 4, 5, 8, 5, 3, 3, 5, 1, -1, -1, -1, -1, -1, -1, -1},
        {0, 4, 5, 1, 0, 5, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {8, 4, 5, 8, 5, 3, 9, 0, 5, 0, 3, 5, -1, -1, -1, -1},
        {9, 4, 5, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {4, 11, 7, 4, 9, 11, 9, 10, 11, -1, -1, -1, -1, -1, -1, -1},
        {0, 8, 3, 4, 9, 7, 9, 11, 7, 9, 10, 11, -1, -1, -1, -1},
        {1, 10, 11, 1, 11, 4, 1, 4, 0, 7, 4, 11, -1, -1, -1, -1},
        {3, 1, 4, 3, 4, 8, 1, 10, 4, 7, 4, 11, 10, 11, 4, -1},
        {4, 11, 7, 9, 11, 4, 9, 2, 11, 9, 1, 2, -1, -1, -1, -1},
        {9, 7, 4, 9, 11, 7, 9, 1, 11, 2, 11, 1, 0, 8, 3, -1},
        {11, 7, 4, 11, 4, 2, 2, 4, 0, -1, -1, -1, -1, -1, -1, -1},
        {11, 7, 4, 11, 4, 2, 8, 3, 4, 3, 2, 4, -1, -1, -1, -1},
        {2, 9, 10, 2, 7, 9, 2, 3, 7, 7, 4, 9, -1, -1, -1, -1},
        {9, 10, 7, 9, 7, 4, 10, 2, 7, 8, 7, 0, 2, 0, 7, -1},
        {3, 7, 10, 3, 10, 2, 7, 4, 10, 1, 10, 0, 4, 0, 10, -1},
        {1, 10, 2, 8, 7, 4, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {4, 9, 1, 4, 1, 7, 7, 1, 3, -1, -1, -1, -1, -1, -1, -1},
        {4, 9, 1, 4, 1, 7, 0, 8, 1, 8, 7, 1, -1, -1, -1, -1},
        {4, 0, 3, 7, 4, 3, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {4, 8, 7, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {9, 10, 8, 10, 11, 8, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {3, 0, 9, 3, 9, 11, 11, 9, 10, -1, -1, -1, -1, -1, -1, -1},
        {0, 1, 10, 0, 10, 8, 8, 10, 11, -1, -1, -1, -1, -1, -1, -1},
        {3, 1, 10, 11, 3, 10, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {1, 2, 11, 1, 11, 9, 9, 11, 8, -1, -1, -1, -1, -1, -1, -1},
        {3, 0, 9, 3, 9, 11, 1, 2, 9, 2, 11, 9, -1, -1, -1, -1},
        {0, 2, 11, 8, 0, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {3, 2, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {2, 3, 8, 2, 8, 10, 10, 8, 9, -1, -1, -1, -1, -1, -1, -1},
        {9, 10, 2, 0, 9, 2, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {2, 3, 8, 2, 8, 10, 0, 1, 8, 1, 10, 8, -1, -1, -1, -1},
        {1, 10, 2, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {1, 3, 8, 9, 1, 8, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {0, 9, 1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {0, 3, 8, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {-1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1}
    };


    public static int[] managed_nbTriTable = new int[]
    {
        0, 3, 3, 6, 3, 6, 6, 9, 3, 6, 6, 9, 6, 9, 9, 6, 3,
        6, 6, 9, 6, 9, 9, 12, 6, 9, 9, 12, 9, 12, 12, 9,
        3, 6, 6, 9, 6, 9, 9, 12, 6, 9, 9, 12, 9, 12, 12,
        9, 6, 9, 9, 6, 9, 12, 12, 9, 9, 12, 12, 9, 12,
        15, 15, 6, 3, 6, 6, 9, 6, 9, 9, 12, 6, 9, 9, 12,
        9, 12, 12, 9, 6, 9, 9, 12, 9, 12, 12, 15, 9, 12,
        12, 15, 12, 15, 15, 12, 6, 9, 9, 12, 9, 12, 6,
        9, 9, 12, 12, 15, 12, 15, 9, 6, 9, 12, 12, 9, 12,
        15, 9, 6, 12, 15, 15, 12, 15, 6, 12, 3, 3, 6, 6,
        9, 6, 9, 9, 12, 6, 9, 9, 12, 9, 12, 12, 9, 6, 9,
        9, 12, 9, 12, 12, 15, 9, 6, 12, 9, 12, 9, 15, 6,
        6, 9, 9, 12, 9, 12, 12, 15, 9, 12, 12, 15, 12,
        15, 15, 12, 9, 12, 12, 9, 12, 15, 15, 12, 12,
        9, 15, 6, 15, 12, 6, 3, 6, 9, 9, 12, 9, 12, 12,
        15, 9, 12, 12, 15, 6, 9, 9, 6, 9, 12, 12, 15,
        12, 15, 15, 6, 12, 9, 15, 12, 9, 6, 12, 3, 9,
        12, 12, 15, 12, 15, 9, 12, 12, 15, 15, 6, 9,
        12, 6, 3, 6, 9, 9, 6, 9, 12, 6, 3, 9, 6, 12, 3,
        6, 3, 3, 0
    };
    #endregion

    private void OnDrawGizmos()
    {
        if (drawPlanetBounds)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawWireCube(new Vector3(LODActualSize / 2f, LODActualSize / 2f, LODActualSize / 2f) + transform.position, new Vector3(LODActualSize, LODActualSize, LODActualSize));
        }
    }
}
