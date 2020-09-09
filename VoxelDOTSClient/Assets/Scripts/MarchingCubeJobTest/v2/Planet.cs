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
using System;

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
    public float basePlayerMovementThreshold;
    float playerMovementThreshold;
    [SerializeField]
    private int maxSplitLOD = 2;
    [SerializeField]
    private int curSplitLOD = 0;

    public int poolSize = 2000;

    [Header("Planet Settings")]
    public float radius;                        //CANNOT BE SMALLER THAN baseChunkSize
    public FastNoiseSIMDUnity noise1;
    public FastNoiseSIMDUnity noise2;
    public int chunksGenerated = 0;
    public Text chunkLoadedText;
    public Text playerVelText;
    public Material chunkMat;
    [SerializeField]
    private GenerateTerrainData terrainGenerator;

    public GameObject playerObj;

    Dictionary<Chunk, Mesh> meshMap;
    public List<Chunk> loadQueue;
    List<Chunk> logicUpdateList;

    Dictionary<int3, Chunk> chunkMap;

    public Queue<GameObject> chunkPool;

    GameObject chunkPrefab;

    NativeArray<float> densities;
    //NativeArray<float> colors;
    NativeArray<float3> curVertices;
    NativeArray<float3> curNormals;
    NativeArray<Color> curColors;
    NativeArray<int> curTriangles;

    NativeArray<int> nbTriTable;
    NativeArray<int> triTable;

    NativeArray<byte> regularCellClass;
    NativeArray<byte> regularCellDataCount;
    NativeArray<byte> regularCellDataTris;
    NativeArray<ushort> regularVertexData;
    NativeArray<int> regularVertexDataVerticesBeforeCurrent;
    NativeArray<int> regularCellDataTrisBeforeCurrent;
    NativeArray<int3> regularCornerIndex;

    NativeArray<byte> transCellClass;
    NativeArray<byte> transCellDataCount;
    NativeArray<byte> transCellDataTris;
    NativeArray<ushort> transVertexData;
    NativeArray<int> transVertexDataVerticesBeforeCurrent;
    NativeArray<int> transCellDataTrisBeforeCurrent;
    NativeArray<int3> transCornerIndex;

    public int totalSize;
    public int curLowerLODLoaded;
    //public int totalSizeNormals;
    float dx;
    float3 originGrid;

    Material material;

    public Vector3 lastPlayerPos;

    void Awake()
    {
        playerMovementThreshold = basePlayerMovementThreshold;
        meshMap = new Dictionary<Chunk, Mesh>(poolSize);
        //chunkList = new NativeList<Entity>(poolSize, Allocator.Persistent);
        //loadQueue = new NativeQueue<Entity>(Allocator.Persistent);

        loadQueue = new List<Chunk>(poolSize);
        chunkMap = new Dictionary<int3, Chunk>(poolSize);
        logicUpdateList = new List<Chunk>(poolSize);

        chunkPool = new Queue<GameObject>(poolSize);

        //totalSize = (baseChunkSize + 1) * (baseChunkSize + 1) * (baseChunkSize + 1);
        totalSize = (baseChunkSize + 3) * (baseChunkSize + 3) * (baseChunkSize + 3);

        originGrid = float3.zero;
        lastPlayerPos = float3.zero;
        dx = 1;
        material = Resources.Load<Material>("Materials/TerrainMat");
        chunkPrefab = Resources.Load<GameObject>("Prefabs/chunkPrefab");
        initTriTable();
        initRegularCellData();
        initTransCellData();

        maxLOD = (byte)Mathf.CeilToInt(Mathf.Log(radius * 2f, 2f) - Mathf.Log(baseChunkSize, 2f));
        minLOD = (byte)0;
        curLowerLODLoaded = maxLOD;

        LODActualSize = (int)Mathf.Pow(2f, maxLOD + Mathf.Log(baseChunkSize, 2f));

        //transform.position = new Vector3(-LODActualSize / 2f, -LODActualSize / 2f - radius, -LODActualSize / 2f);

        //transform.position = new float3(0f, -radius, 0f);

        for (int i = 0; i < poolSize; i++)
        {
            GameObject spawnedChunk = Instantiate(chunkPrefab);
            spawnedChunk.GetComponent<Chunk>().enabled = false;
            spawnedChunk.transform.SetParent(transform);
            chunkPool.Enqueue(spawnedChunk);

            spawnedChunk.GetComponent<Chunk>().values.isDoneLoading = false;
            spawnedChunk.GetComponent<Chunk>().values.wireColor = Color.red;
            //spawnedChunk.GetComponent<Chunk>().densities = new float[totalSize];

            Mesh newMesh = new Mesh();
            newMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            spawnedChunk.GetComponent<MeshFilter>().mesh = newMesh;
            spawnedChunk.GetComponent<MeshRenderer>().material = chunkMat;
            spawnedChunk.GetComponent<MeshCollider>().sharedMesh = newMesh;
            spawnedChunk.GetComponent<Chunk>().borderRingChunk.GetComponent<MeshRenderer>().material = chunkMat;

            //meshMap.Add(spawnedChunk.GetComponent<Chunk>(), newMesh);
        }
    }

    void Start()
    {
        for (int i = 0; i < 1; i++) //For loop was for debugging
        {
            GameObject tempChunk = GetChunk();

            tempChunk.transform.localPosition = new float3(-LODActualSize / 2f);

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
            chunk.values.lastParentList = int3.zero;

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

        if (regularCellClass.IsCreated)
            regularCellClass.Dispose();

        if (regularCellDataCount.IsCreated)
            regularCellDataCount.Dispose();

        if (regularVertexData.IsCreated)
            regularVertexData.Dispose();

        if (regularVertexDataVerticesBeforeCurrent.IsCreated)
            regularVertexDataVerticesBeforeCurrent.Dispose();

        if (regularCellDataTris.IsCreated)
            regularCellDataTris.Dispose();

        if (regularCellDataTrisBeforeCurrent.IsCreated)
            regularCellDataTrisBeforeCurrent.Dispose();

        if (transCellClass.IsCreated)
            transCellClass.Dispose();

        if (transCellDataCount.IsCreated)
            transCellDataCount.Dispose();

        if (transVertexData.IsCreated)
            transVertexData.Dispose();

        if (transVertexDataVerticesBeforeCurrent.IsCreated)
            transVertexDataVerticesBeforeCurrent.Dispose();

        if (transCellDataTris.IsCreated)
            transCellDataTris.Dispose();

        if (transCellDataTrisBeforeCurrent.IsCreated)
            transCellDataTrisBeforeCurrent.Dispose();

        if (transCornerIndex.IsCreated)
            transCornerIndex.Dispose();

        if (regularCornerIndex.IsCreated)
            regularCornerIndex.Dispose();
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

        //float startTime = Time.realtimeSinceStartup;

        for (int i = 0; i < threadCount; i++)
        {
            if (updateTimer <= 0f && loadQueue.Count > 0)
            {
                loadQueue = loadQueue.OrderBy(w => w.values.distanceFromPlayer).ToList();
                //loadQueue = loadQueue.OrderBy(w => w.values.lod).Reverse().ToList();

                updateTimer = 0f;


                GenerateMesh(loadQueue[0]);

                loadQueue[0] = loadQueue[loadQueue.Count - 1];

                loadQueue.RemoveAt(loadQueue.Count - 1);

                //GenerateMesh(loadQueue.Dequeue());


            }
        }

        //float elapsedTime = (Time.realtimeSinceStartup - startTime);

        //Debug.Log("Took: " + elapsedTime * 1000f + "ms");

        chunkLoadedText.text = chunksGenerated.ToString();

        LODDistanceManiupulation();

        if (logicUpdateList.Count > 0 && logicTimer <= 0f)
        {
            //logicTimer = logicTime;
            logicTimer = 0f;
            ChunkUpdateLogic();
        }

        //Debug.Log("asdasd");
    }

    Chunk curChunk;
    float3 center;
    float3 playerPos;
    Vector3 playerPosLastFrame = Vector3.zero;
    float3 playerVel;
    [SerializeField]
    float playerVelMag;
    float sqrDistance;
    bool childrenAreDone;
    bool siblingsSameLOD;
    int3 tempVector;
    Vector3 parentCenter;
    Bounds parentBounds;

    float viewDistanceMultiplier;
    float joinDistanceMultiplier;

    void LODDistanceManiupulation()
    {
        //playerVel = playerObj.GetComponent<Rigidbody>().velocity * 2f;
        playerVel = (playerObj.transform.position - playerPosLastFrame) / Time.deltaTime;
        playerVel /= 2f;
        playerVelMag = (playerObj.transform.position - playerPosLastFrame).magnitude / Time.deltaTime;
        playerPosLastFrame = playerObj.transform.position;

        if (playerVelMag > 2200f)
        {
            logicTime = 1f;
            minLOD = 6;
            //viewDistanceMultiplier = 10f;
            //joinDistanceMultiplier = 18f;

            playerMovementThreshold = basePlayerMovementThreshold * 40f;
        }
        else if (playerVelMag > 1400f)
        {
            logicTime = 1f;
            minLOD = 3;
            //viewDistanceMultiplier = 8f;
            //joinDistanceMultiplier = 15f;

            playerMovementThreshold = basePlayerMovementThreshold * 22f;
        }
        else if (playerVelMag > 800f)
        {
            logicTime = 1f;
            minLOD = 2;
            viewDistanceMultiplier = 3f;
            joinDistanceMultiplier = 10f;

            playerMovementThreshold = basePlayerMovementThreshold * 16f;
        }
        else if (playerVelMag > 400f)
        {
            logicTime = 0.5f;
            minLOD = 1;
            viewDistanceMultiplier = 2f;
            joinDistanceMultiplier = 9f;

            playerMovementThreshold = basePlayerMovementThreshold * 8f;
        }
        else if (playerVelMag > 100f)
        {
            logicTime = 0.05f;
            minLOD = 0;
            viewDistanceMultiplier = 1f;
            joinDistanceMultiplier = 8f;

            playerMovementThreshold = basePlayerMovementThreshold * 2f;
        }
        else
        {
            logicTime = 0.01f;
            minLOD = 0;
            viewDistanceMultiplier = 1f;
            joinDistanceMultiplier = 8f;

            playerMovementThreshold = basePlayerMovementThreshold * 1f;
        }

        if (Vector3.Distance(playerObj.transform.position, lastPlayerPos) > playerMovementThreshold)
        {
            playerPos = playerObj.transform.position;
            lastPlayerPos = playerObj.transform.position;
        }
    }

    void ChunkUpdateLogic()
    {
        float startTime = Time.realtimeSinceStartup;

        playerVelText.text = playerObj.GetComponent<Rigidbody>().velocity.magnitude.ToString();

        curLowerLODLoaded = maxLOD;

        for (int i = 0; i < logicUpdateList.Count; i++)
        {
            curChunk = logicUpdateList[i];

            if (curChunk.values.lod < curLowerLODLoaded)
                curLowerLODLoaded = curChunk.values.lod;

            Vector3 center = Vector3.one * curChunk.values.actualChunkSizeHalf;
            Bounds bounds = new Bounds(center + curChunk.transform.position, Vector3.one * curChunk.values.actualChunkSize);

            sqrDistance = bounds.SqrDistance(playerPos + playerVel);
            curChunk.values.distanceFromPlayer = sqrDistance;

            if (!curChunk.values.hasJoined && !curChunk.values.hasSplit && (curChunk.values.lastParentList.x == 0 && curChunk.values.lastParentList.y == 0 && curChunk.values.lastParentList.z == 0) && curChunk.values.lod < maxLOD && curChunk.parentFromSplit == null)
            {
                siblingsSameLOD = true;

                for (int x = 0; x < 2; x++)
                {
                    for (int y = 0; y < 2; y++)
                    {
                        for (int z = 0; z < 2; z++)
                        {
                            tempVector = new int3(x, y, z);

                            Chunk siblingChunk = chunkMap[tempVector * curChunk.values.actualChunkSize + curChunk.values.chunkPos];

                            if (siblingChunk.values.lod != curChunk.values.lod || !siblingChunk.values.isDoneLoading || siblingChunk.values.hasJoined || siblingChunk.values.hasSplit)
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

                    float parentSqrDistance = parentBounds.SqrDistance(playerPos);

                    if (ShouldJoinDistance(parentSqrDistance, curChunk.values.actualChunkSize, joinDistanceMultiplier))
                    {
                        curChunk.values.hasJoined = false;

                        JoinChunks(curChunk);
                        //continue;
                    }
                }
            }

            if (!curChunk.values.hasSplit && curChunk.values.hasJoined && curChunk.values.isDoneLoading)
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

                    curChunk.borderRingChunk.GetComponent<MeshRenderer>().enabled = false;

                    curChunk.GetComponent<MeshCollider>().enabled = false;
                    curChunk.GetComponent<MeshRenderer>().enabled = false;

                    logicUpdateList.RemoveAt(i);

                    RemoveChunk(curChunk);
                }
            }

            if (curChunk.values.isDoneLoading && ((curChunk.parentFromSplit == null && curChunk.values.isChild) || !curChunk.values.isChild) && !curChunk.values.hasSplit && !curChunk.values.hasJoined)// && (!curChunk.values.isEmpty && !curChunk.values.isFull)
            {
                if (curChunk.values.markedToSplit || ShouldSplitDistance(sqrDistance, curChunk.values.actualChunkSize, curChunk.values.isEmpty, curChunk.values.isFull, minLOD, curChunk.values.lod, viewDistanceMultiplier))
                {
                    curChunk.values.hasSplit = true;
                    SplitChunk(curChunk, false);
                    //ShouldSplitLOD()
                }
            }
        }

        //Debug.Log("Logic: " + (Time.realtimeSinceStartup - startTime) * 1000f + "ms");
    }

    void GenerateMesh(Chunk entity)
    {
        entity.values.isFull = false;
        entity.values.isEmpty = false;


        if (curVertices.IsCreated)
            curVertices.Dispose();
        if (curNormals.IsCreated)
            curNormals.Dispose();
        if (curTriangles.IsCreated)
            curTriangles.Dispose();
        if (densities.IsCreated)
            densities.Dispose();

        //CountVertexPerVoxelJob
        NativeArray<uint2> vertPerCellIn = new NativeArray<uint2>(totalSize, Allocator.TempJob);
        NativeArray<uint2> vertPerCell = new NativeArray<uint2>(totalSize, Allocator.TempJob);
        NativeArray<uint> compactedVoxel = new NativeArray<uint>(totalSize, Allocator.TempJob);

        float scaleFactor = entity.values.actualChunkSize / baseChunkSize;

        if (!entity.values.isDoneLoading)
        {
            densities = new NativeArray<float>(totalSize, Allocator.TempJob);

            if(!entity.densities.IsCreated)
                entity.densities = new NativeArray<float>(totalSize, Allocator.Persistent);

            var generateTerrainDataJob = new MarchingCubeJobs.ComputeTerrainDataJob()
            {
                densV = densities,
                baseChunkSize = baseChunkSize + 3,
                totalSize = totalSize,
                isoValue = isoValue,
                radius = radius,
                chunkPos = entity.values.chunkPos,
                LODActualSize = LODActualSize,
                scaleFactor = scaleFactor,
                normalOffset = new int3(1)
                //densIn = densIn
            };

            var generateTerrainDataJobHandle = generateTerrainDataJob.Schedule(totalSize, batchSize / 4);
            generateTerrainDataJobHandle.Complete();

            densities.CopyTo(entity.densities);
        }
        else
        {
            densities = new NativeArray<float>(totalSize, Allocator.TempJob);

            entity.densities.CopyTo(densities);
        }


        /*if (entity.values.isEmpty || entity.values.isFull)
        {
            //Debug.Log("empty 1");

            entity.values.isDoneLoading = true;

            vertPerCell.Dispose();
            compactedVoxel.Dispose();
            vertPerCellIn.Dispose();
            Clean();

            return;
        }*/


        var countVJob = new MarchingCubeJobs.CountVertexPerVoxelJob()
        {
            densV = densities,
            nbTriTable = nbTriTable,
            triTable = triTable,
            vertPerCell = vertPerCellIn,
            chunkSize = baseChunkSize + 3,
            totalVoxel = totalSize,
            isoValue = isoValue,
            normalOffset = new int3(1)
        };

        var countVJobHandle = countVJob.Schedule(totalSize, batchSize);
        countVJobHandle.Complete();


        //exclusivescan => compute the total number of vertices
        uint2 lastElem = vertPerCellIn[totalSize - 1];


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
            entity.values.isFull = true;
            entity.values.isEmpty = true;
            vertPerCell.Dispose();
            compactedVoxel.Dispose();
            vertPerCellIn.Dispose();
            Clean();
            return;
        }

        //chunksGenerated++;

        entity.values.drawBounds = true;

        curVertices = new NativeArray<float3>((int)totalVerts, Allocator.Persistent);
        curNormals = new NativeArray<float3>((int)totalVerts, Allocator.Persistent);
        //curColors = new NativeArray<Color>((int)totalVerts, Allocator.Persistent);
        //Double the triangles to have both faces
        curTriangles = new NativeArray<int>((int)totalVerts, Allocator.Persistent);

        //compactvoxels

        var compactJob = new MarchingCubeJobs.CompactVoxelJob()
        {
            vertPerCell = vertPerCell,
            compVoxel = compactedVoxel,
            //chunkSize = baseChunkSize + 3,
            totalVoxel = totalSize,
            lastElem = lastElem.y
        };

        var compactJobHandle = compactJob.Schedule(totalSize, batchSize, escanJobJobHandle);
        compactJobHandle.Complete();


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
            chunkSize = baseChunkSize + 3,
            isoValue = isoValue,
            totalVerts = totalVerts,
            vertexScale = entity.values.vertexScale,
            normalOffset = new int3(1)
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
            chunkSize = baseChunkSize + 3,
            vertexScale = entity.values.vertexScale,
            normalOffset = new int3(1)
        };
        var NormJobHandle = NormJob.Schedule((int)totalVerts, batchSize, MCJobHandle);
        NormJobHandle.Complete();

        //TEMP COLORS
        /*var ColorJob = new MarchingCubeJobs.ComputeColorsJobTEMP()
        {
            colors = curColors,
            vertices = curVertices,
            densV = densities,
            radius = radius,
            actualChunkSize = entity.values.actualChunkSize,
            chunkPos = entity.values.chunkPos,
            LODActualSize = LODActualSize,
            baseChunkSize = baseChunkSize,
            lod = entity.values.lod,
            oriGrid = originGrid,
            dx = dx,
            chunkSize = baseChunkSize + 1,
            vertexScale = entity.values.vertexScale
        };
        var ColorJobHandle = ColorJob.Schedule((int)totalVerts, batchSize, NormJobHandle);
        ColorJobHandle.Complete();*/

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

        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        mesh.SetVertices(curVertices);
        mesh.triangles = getTriangles();

        //mesh.SetColors(curColors);

        if (smoothNormals)
            mesh.SetNormals(curNormals);
        else
            mesh.RecalculateNormals();

        entity.GetComponent<MeshCollider>().sharedMesh = mesh;
        entity.GetComponent<MeshFilter>().mesh = mesh;

        //if (entity.densities.Length == 0)
        //    entity.densities = new float[densities.Length];

        //SetNativeDensityArray(entity.densities, densities);

        entity.values.isDoneLoading = true;

        //chunksGenerated++;

        if(entity.values.lod < maxLOD && entity.values.lod > 0)
        {
            GenerateBorderChunk(entity);
        }

        Clean();

        if (entity.values.lod < maxLOD && entity.values.lod > 0)
        {
            
        }
    }

    void GenerateBorderChunk(Chunk entity)
    {
        if (curVertices.IsCreated)
            curVertices.Dispose();
        if (curNormals.IsCreated)
            curNormals.Dispose();
        if (curTriangles.IsCreated)
            curTriangles.Dispose();

        NativeArray<uint2> vertPerCellIn = new NativeArray<uint2>(totalSize, Allocator.TempJob);
        NativeArray<uint2> vertPerCell = new NativeArray<uint2>(totalSize, Allocator.TempJob);
        NativeArray<uint> compactedVoxel = new NativeArray<uint>(totalSize, Allocator.TempJob);

        float scaleFactor = entity.values.actualChunkSize / baseChunkSize;

        var countVJob = new MarchingCubeJobs.CountVertexPerVoxelJob()
        {
            densV = densities,
            nbTriTable = nbTriTable,
            triTable = triTable,
            vertPerCell = vertPerCellIn,
            chunkSize = baseChunkSize + 3,
            totalVoxel = totalSize,
            isoValue = isoValue,
            isBorderChunk = true,
            normalOffset = new int3(1)
        };

        var countVJobHandle = countVJob.Schedule(totalSize, batchSize);
        countVJobHandle.Complete();


        //exclusivescan => compute the total number of vertices
        uint2 lastElem = vertPerCellIn[totalSize - 1];


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
            vertPerCell.Dispose();
            compactedVoxel.Dispose();
            vertPerCellIn.Dispose();
            return;
        }

        //chunksGenerated++;

        curVertices = new NativeArray<float3>((int)totalVerts, Allocator.Persistent);
        curNormals = new NativeArray<float3>((int)totalVerts, Allocator.Persistent);
        curColors = new NativeArray<Color>((int)totalVerts, Allocator.Persistent);
        //Double the triangles to have both faces
        curTriangles = new NativeArray<int>((int)totalVerts, Allocator.Persistent);

        //compactvoxels

        var compactJob = new MarchingCubeJobs.CompactVoxelJob()
        {
            vertPerCell = vertPerCell,
            compVoxel = compactedVoxel,
            //chunkSize = baseChunkSize + 3,
            totalVoxel = totalSize,
            lastElem = lastElem.y
        };

        var compactJobHandle = compactJob.Schedule(totalSize, batchSize, escanJobJobHandle);
        compactJobHandle.Complete();


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
            chunkSize = baseChunkSize + 3,
            isoValue = isoValue,
            totalVerts = totalVerts,
            vertexScale = entity.values.vertexScale,
            normalOffset = new int3(1)
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
            chunkSize = baseChunkSize + 3,
            vertexScale = entity.values.vertexScale,
            normalOffset = new int3(1)
        };
        var NormJobHandle = NormJob.Schedule((int)totalVerts, batchSize, MCJobHandle);
        NormJobHandle.Complete();

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

        entity.borderRingChunk.GetComponent<MeshRenderer>().enabled = true;

        if (entity.borderRingChunk.GetComponent<MeshFilter>().mesh != null)
            mesh = entity.borderRingChunk.GetComponent<MeshFilter>().mesh;
        else
            mesh = new Mesh();

        mesh.Clear();

        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        mesh.SetVertices(curVertices);
        mesh.triangles = getTriangles();

        //mesh.SetColors(curColors);

        if (smoothNormals)
            mesh.SetNormals(curNormals);
        else
            mesh.RecalculateNormals();

        entity.borderRingChunk.GetComponent<MeshFilter>().mesh = mesh;
    }

    void SplitChunk(Chunk parentChunk, bool isLODSplit)
    {

        //logicUpdateList.RemoveAt(index);

        float splitTimer = Time.realtimeSinceStartup;

        //chunkMap.Remove(parentChunk.values.chunkPos);

        //parentChunk.transChunksShouldLoadCheck = 255;

        ShouldSplitLOD(parentChunk);

        chunksGenerated--;

        /*if (loadQueue.Contains(parentChunk))
            loadQueue.Remove(parentChunk);*/

        byte childIndex = 0;

        for (int x = 0; x < 2; x++)
        {
            for (int y = 0; y < 2; y++)
            {
                for (int z = 0; z < 2; z++)
                {
                    chunksGenerated++;

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

                    chunk.values.isEmpty = parentChunk.values.isEmpty;
                    chunk.values.isFull = parentChunk.values.isFull;

                    chunk.values.distanceFromPlayer = Mathf.Infinity;
                    chunk.planet = this;

                    chunk.parentFromSplit = parentChunk;
                    chunk.values.indexFromSplit = childIndex;

                    chunk.parentList = new int3[parentChunk.parentList.Length + 1];
                    parentChunk.parentList.CopyTo(chunk.parentList, 0);
                    chunk.parentList[chunk.parentList.Length - 1] = new int3(x, y, z);
                    chunk.values.lastParentList = new int3(x, y, z);

                    tempChunk.transform.localPosition = new float3(-LODActualSize / 2f) + new float3(x * chunk.values.actualChunkSize, y * chunk.values.actualChunkSize, z * chunk.values.actualChunkSize) + parentChunk.values.chunkPos;
                    tempChunk.transform.localRotation = Quaternion.identity;

                    //tempChunk.transform.SetParent(null);

                    chunk.values.chunkPos = new int3(x * chunk.values.actualChunkSize, y * chunk.values.actualChunkSize, z * chunk.values.actualChunkSize) + parentChunk.values.chunkPos;


                    //chunk.transChunksShouldLoadCheck = 4;

                    /*for (int j = 0; j < 6; j++)
                    {
                        if (!parentChunk.borderChunkTrans[j].Equals(new int3(-2)))
                        {
                            int3 localPos = (new int3(x, y, z) * 2) - 1;
                            int3 localPos2 = new int3(localPos.x * math.abs(parentChunk.borderChunkTrans[j].x), localPos.y * math.abs(parentChunk.borderChunkTrans[j].y), localPos.z * math.abs(parentChunk.borderChunkTrans[j].z));

                            int3 pos = new int3(parentChunk.values.chunkPos.x + (parentChunk.borderChunkTrans[j].x * parentChunk.values.actualChunkSize), parentChunk.values.chunkPos.y + (parentChunk.borderChunkTrans[j].y * parentChunk.values.actualChunkSize), parentChunk.values.chunkPos.z + (parentChunk.borderChunkTrans[j].z * parentChunk.values.actualChunkSize));

                            int multiplierX = (parentChunk.borderChunkTrans[j].x * (parentChunk.borderChunkTrans[j].x > 0 ? chunk.values.actualChunkSize : chunk.values.actualChunkSize * 2));
                            int multiplierY = (parentChunk.borderChunkTrans[j].y * (parentChunk.borderChunkTrans[j].y > 0 ? chunk.values.actualChunkSize : chunk.values.actualChunkSize * 2));
                            int multiplierZ = (parentChunk.borderChunkTrans[j].z * (parentChunk.borderChunkTrans[j].z > 0 ? chunk.values.actualChunkSize : chunk.values.actualChunkSize * 2));

                            if (localPos2.Equals(parentChunk.borderChunkTrans[j]))
                            {
                                Debug.Log("correct");

                                chunk.transChunksShouldLoadCheck |= (byte)(1 << j);
                            }
                        }

                        //if(j == 2 || j == 3 || j == 4|| j == 5)
                            //chunk.transChunksShouldLoadCheck |= (byte)(1 << j);
                    }*/

                    //chunk.borderChunkTrans = (int3[])parentChunk.borderChunkTrans.Clone();

                    /*int3 xNeighbor = new int3(parentChunk.values.chunkPos.x + (parentChunk.borderChunkTrans[0] * parentChunk.values.actualChunkSize), parentChunk.values.chunkPos.y, parentChunk.values.chunkPos.z);
                    int3 yNeighbor = new int3(parentChunk.values.chunkPos.x, parentChunk.values.chunkPos.x + (parentChunk.borderChunkTrans[1] * parentChunk.values.actualChunkSize), parentChunk.values.chunkPos.z);
                    int3 zNeighbor = new int3(parentChunk.values.chunkPos.x, parentChunk.values.chunkPos.y, parentChunk.values.chunkPos.z + (parentChunk.borderChunkTrans[2] * parentChunk.values.actualChunkSize));

                    chunk.transChunksShouldLoadCheck = 0;

                    if (parentChunk.borderChunkTrans[0] != -2 && chunk.values.chunkPos.x == xNeighbor.x - (parentChunk.borderChunkTrans[0] * (parentChunk.borderChunkTrans[0] > 0 ? chunk.values.actualChunkSize : chunk.values.actualChunkSize * 2)))
                    {
                        Debug.Log("1 lod different: x");

                        if (parentChunk.borderChunkTrans[0] > 0)
                        {
                            chunk.transChunksShouldLoadCheck = 1 << 2;
                        }
                        else
                        {
                            chunk.transChunksShouldLoadCheck = 1 << 3;
                        }
                    }

                    if (parentChunk.borderChunkTrans[1] != -2 && chunk.values.chunkPos.y == yNeighbor.y - (parentChunk.borderChunkTrans[1] * (parentChunk.borderChunkTrans[1] > 0 ? chunk.values.actualChunkSize : chunk.values.actualChunkSize * 2)))
                    {
                        Debug.Log("1 lod different: y");

                        if (parentChunk.borderChunkTrans[1] > 0)
                        {
                            chunk.transChunksShouldLoadCheck |= 1 << 0;
                        }
                        else
                        {
                            chunk.transChunksShouldLoadCheck |= 1 << 1;
                        }
                    }

                    if (parentChunk.borderChunkTrans[2] != -2 && chunk.values.chunkPos.z == zNeighbor.z - (parentChunk.borderChunkTrans[2] * (parentChunk.borderChunkTrans[2] > 0 ? chunk.values.actualChunkSize : chunk.values.actualChunkSize * 2)))
                    {
                        Debug.Log("1 lod different: z");

                        if (parentChunk.borderChunkTrans[2] > 0)
                        {
                            chunk.transChunksShouldLoadCheck |= 1 << 4;
                        }
                        else
                        {
                            chunk.transChunksShouldLoadCheck |= 1 << 5;
                        }
                    }*/

                    /*chunkMap.Add(chunk.values.chunkPos, chunk);
                    logicUpdateList.Add(chunk);*/

                    if (!chunk.values.isEmpty && !chunk.values.isFull)
                        loadQueue.Add(chunk);
                    else
                        chunk.values.isDoneLoading = true;

                    parentChunk.childrenFromSplit[childIndex] = chunk;

                    childIndex++;
                }
            }
        }

        if (isLODSplit)
            curSplitLOD--;

        //Debug.Log("Split: " + (Time.realtimeSinceStartup - splitTimer) * 1000f + "ms");
    }

    static bool ShouldSplitDistance(float distance, int actualChunkSize, bool isEmpty, bool isFull, byte minLOD, byte lod, float viewDistanceMultiplier)
    {
        bool result = false;

        float checkDist = ((float)actualChunkSize / 2f) * viewDistanceMultiplier + 25f;

        if (distance < (checkDist * checkDist) && lod > minLOD)//&& (!isEmpty && !isFull)
        {
            result = true;
        }

        return result;
    }

    static bool ShouldJoinDistance(float distance, int actualChunkSize, float joinDistanceMultiplier)
    {
        bool join = false;

        float checkDist = ((float)actualChunkSize * joinDistanceMultiplier) + 80f;

        if (distance > (checkDist * checkDist))
        {
            join = true;
        }

        return join;
    }

    //int3 parentPos;
    bool needToSplit = true;

    void ShouldSplitLOD(Chunk chunk)
    {
        if (chunk.values.lod < maxLOD - 1)
        {
            //parentPos = chunk.values.chunkPos - chunk.parentList[chunk.parentList.Length - 1] * chunk.values.actualChunkSize;

            int3[] sides = GetAdjacentSides(chunk.values.chunkPos, chunk.values.actualChunkSize, chunk.parentList[chunk.parentList.Length - 1]);
            int[] sidesDirection = GetAdjacentSidesWithoutChunksize(chunk.values.chunkPos, chunk.values.actualChunkSize, chunk.parentList[chunk.parentList.Length - 1]);

            //Vector3Int[] parentSides = GetAdjacentSidesParent(chunk.chunkPos, chunk.actualChunkSize * 2f, chunk.parentList[chunk.parentList.Count - 2]);

            for (int i = 0; i < sides.Length; i++)
            {
                needToSplit = true;

                Chunk curSide;
                Chunk curParent;
                int3 curParentPos = GetParentChunk(sides[i], chunk.values.actualChunkSize * 2);

                if (chunkMap.TryGetValue(sides[i], out curSide) && curSide.values.lod <= chunk.values.lod)
                {
                    needToSplit = false;

                    //Debug.Log("DONT SPLIT");
                }
                else
                {
                    //Debug.Log("WHY SPLIT");
                }

                if (needToSplit && IsInBounds(curParentPos, LODActualSize) && chunkMap.TryGetValue(curParentPos, out curParent))
                {
                    //TODO: Experiment with allowing multiple chunks to split per frame. Too many, slow performance; too few, it takes longer for chunks load(split)

                    if (!(curParent.values.isEmpty || curParent.values.isFull))
                    {
                        curParent.values.markedToSplit = true;
                    }

                    //Debug.Log("SPLIT");

                }
            }
        }
    }

    void SplitTransvoxelCheck(Chunk chunk)
    {
        if (chunk.values.lod < maxLOD - 1)
        {
            //parentPos = chunk.values.chunkPos - chunk.parentList[chunk.parentList.Length - 1] * chunk.values.actualChunkSize;

            int3[] sides = GetAdjacentSides(chunk.values.chunkPos, chunk.values.actualChunkSize, chunk.parentList[chunk.parentList.Length - 1]);
            //Vector3Int[] parentSides = GetAdjacentSidesParent(chunk.chunkPos, chunk.actualChunkSize * 2f, chunk.parentList[chunk.parentList.Count - 2]);

            for (int i = 0; i < sides.Length; i++)
            {
                needToSplit = true;

                Chunk curSide;
                Chunk curParent;
                int3 curParentPos = GetParentChunk(sides[i], chunk.values.actualChunkSize * 2);

                if (chunkMap.TryGetValue(sides[i], out curSide) && curSide.values.lod <= chunk.values.lod)
                {
                    needToSplit = false;
                    //Debug.Log("DONT SPLIT");
                }
                else
                {
                    //Debug.Log("WHY SPLIT");
                }

                if (needToSplit && IsInBounds(curParentPos, LODActualSize) && chunkMap.TryGetValue(curParentPos, out curParent))
                {
                    //TODO: Experiment with allowing multiple chunks to split per frame. Too many, slow performance; too few, it takes longer for chunks load(split)

                    curParent.values.markedToSplit = true;
                    //Debug.Log("SPLIT");

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
                }
            }
        }

        bool isEmpty = true;
        bool isFull = true;

        for (int i = 0; i < 8; i++)
        {
            chunkSiblings[i].values.drawBounds = false;
            chunkSiblings[i].borderRingChunk.GetComponent<MeshRenderer>().enabled = false;

            if (!chunkSiblings[i].values.isEmpty)
                isEmpty = false;

            if (!chunkSiblings[i].values.isFull)
                isFull = false;

            for (int j = 0; j < 6; j++)
            {
                //chunkSiblings[i].transChunks[i].GetComponent<MeshRenderer>().enabled = false;
            }

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

        chunk.parentList = new int3[mainSibling.parentList.Length - 1];
        System.Array.Copy(mainSibling.parentList, 0, chunk.parentList, 0, mainSibling.parentList.Length - 1);
        chunk.values.lastParentList = chunk.parentList[chunk.parentList.Length - 1];

        tempChunk.transform.localPosition = new float3(-LODActualSize / 2f) + chunkPos;
        tempChunk.transform.localRotation = Quaternion.identity;


        chunk.values.chunkPos = mainSibling.values.chunkPos;
        chunk.values.isEmpty = isEmpty;
        chunk.values.isFull = isFull;

        chunkMap.Add(chunk.values.chunkPos, chunk);

        if (!isEmpty && !isFull)
            loadQueue.Add(chunk);
        else
            chunk.values.isDoneLoading = true;

        logicUpdateList.Add(chunk);

        Chunk[] temp = (Chunk[])chunkSiblings.ToArray().Clone();
        chunk.childrenFromJoin = temp.ToList();

        chunk.values.hasJoined = true;

        chunksGenerated++;
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

    unsafe NativeArray<float> GetNativeDensityArrays(float[] densityValues)
    {
        // create a destination NativeArray to hold the vertices
        NativeArray<float> verts = new NativeArray<float>(densityValues.Length, Allocator.Persistent,
            NativeArrayOptions.UninitializedMemory);

        // pin the mesh's vertex buffer in place...
        fixed (void* vertexBufferPointer = densityValues)
        {
            // ...and use memcpy to copy the Vector3[] into a NativeArray<floar3> without casting. whould be fast!
            UnsafeUtility.MemCpy(NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(verts),
                vertexBufferPointer, densityValues.Length * (long)UnsafeUtility.SizeOf<float>());
        }
        // we only hve to fix the .net array in place, the NativeArray is allocated in the C++ side of the engine and
        // wont move arround unexpectedly. We have a pointer to it not a reference! thats basically what fixed does,
        // we create a scope where its 'safe' to get a pointer and directly manipulate the array

        return verts;
    }

    public float GetDensity(Vector3 pos)
    {
        int3 localPos = new int3(pos - (transform.position - new Vector3(LODActualSize / 2f, LODActualSize / 2f, LODActualSize / 2f)));

        int3 chunkPos = new int3(math.floor(localPos / (baseChunkSize)) * (baseChunkSize));
        int3 localChunkPos = localPos % (baseChunkSize);

        //Debug.Log("ChunkPos: " + chunkPos);

        return chunkMap[chunkPos].GetDensity(localChunkPos + new int3(1));
    }

    public List<Chunk> SetDensity(Vector3 pos, float density)
    {
        int3 localPos1 = new int3(pos - (transform.position - new Vector3(LODActualSize / 2f, LODActualSize / 2f, LODActualSize / 2f)));

        int3 lastChunkPos = new int3(math.floor(localPos1 / baseChunkSize) * baseChunkSize);

        List<Chunk> chunks = new List<Chunk>();
        HashSet<Chunk> chunkSet = new HashSet<Chunk>();

        for (int i = 0; i < 8; i++)
        {
            int3 chunkPos = new int3(math.floor((localPos1 - ((LookupTables.CornerIndex[i] * 2))) / baseChunkSize) * baseChunkSize);

            if (i != 0 && chunkPos.Equals(lastChunkPos))
            {
                continue;
            }

            Chunk chunk = chunkMap[chunkPos];

            lastChunkPos = chunkPos;

            int3 localPos = (localPos1 - (chunkPos - new int3(1)));

            chunk.SetDensity(localPos, density);

            if (localPos.x == baseChunkSize)
            {
                int3 newChunkPos = chunkPos + new int3(baseChunkSize, 0, 0);
                chunkMap[newChunkPos].SetDensity((localPos1 - (newChunkPos - new int3(1))), density);
            }

            if (localPos.y == baseChunkSize)
            {
                int3 newChunkPos = chunkPos + new int3(0, baseChunkSize, 0);
                chunkMap[newChunkPos].SetDensity((localPos1 - (newChunkPos - new int3(1))), density);
            }

            if (localPos.z == baseChunkSize)
            {
                int3 newChunkPos = chunkPos + new int3(0, 0, baseChunkSize);
                chunkMap[newChunkPos].SetDensity((localPos1 - (newChunkPos - new int3(1))), density);
            }

            //loadQueue.Add(chunk);

            if (!chunkSet.Contains(chunk))
            {
                chunkSet.Add(chunk);
                chunks.Add(chunk);
            }

            //client.SendEditTerrain(chunk.position, localPos, density);
            //if (setReadyForUpdate)
            //    chunk.readyForUpdate = true;
        }

        return chunks;

        //loadQueue.Add(chunk);
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
            //chunk.GetComponent<Chunk>().densities = new float[totalSize];

            Mesh newMesh = new Mesh();
            newMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            chunk.GetComponent<MeshFilter>().mesh = newMesh;
            chunk.GetComponent<MeshCollider>().sharedMesh = newMesh;
            chunk.GetComponent<MeshRenderer>().material = chunkMat;
            chunk.GetComponent<Chunk>().borderRingChunk.GetComponent<MeshRenderer>().material = chunkMat;

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
        //chunk.GetComponent<Chunk>().values.markedToUpdateTransvoxel = false;
        chunk.GetComponent<Chunk>().values.isChild = false;
        chunk.GetComponent<Chunk>().values.indexForUpdate = 0;
        chunk.GetComponent<Chunk>().values.indexFromSplit = 0;

       // Array.Clear(chunk.GetComponent<Chunk>().densities, 0, totalSize);

        chunk.GetComponent<Chunk>().transChunksShouldLoadCheck = 0;
        chunk.GetComponent<Chunk>().transChunksLoaded = 0;

        chunk.GetComponent<MeshRenderer>().enabled = false;
        chunk.GetComponent<MeshCollider>().enabled = false;

        return chunk;
    }

    void RemoveChunk(Chunk chunk)
    {
        if (chunk.GetComponent<Chunk>().densities.IsCreated)
            chunk.GetComponent<Chunk>().densities.Dispose();

        chunk.GetComponent<MeshFilter>().mesh.Clear();
        chunk.GetComponent<MeshCollider>().sharedMesh.Clear();

        //chunk.transform.SetParent(transform);

        chunk.borderRingChunk.GetComponent<MeshFilter>().mesh.Clear();
        //chunk.borderRingChunk.GetComponent<MeshCollider>().sharedMesh.Clear();

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

    static int[] GetAdjacentSidesWithoutChunksize(int3 chunkPos, int realChunkSize, int3 localPos)
    {
        int[] result = new int[3];

        int x = (int)(localPos.x * 2f) - 1;
        int y = (int)(localPos.y * 2f) - 1;
        int z = (int)(localPos.z * 2f) - 1;

        result[0] = x;
        result[1] = y;
        result[2] = z;

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
        if (curColors.IsCreated)
            curColors.Dispose();
    }

    public static readonly int3[] TransvoxelPositionIndex = new[]
    {
            new int3(0,1,0),
            new int3(0,-1,0),
            new int3(1,0,0),
            new int3(-1,0,0),
            new int3(0,0,1),
            new int3(0,0,-1)
    };

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

    void initRegularCellData()
    {
        regularVertexDataVerticesBeforeCurrent = new NativeArray<int>(256, Allocator.Persistent);
        regularCellDataTrisBeforeCurrent = new NativeArray<int>(16, Allocator.Persistent);

        int regularCellTotalVerts = 0;
        int regularVertexDataTotalVerts = 0;

        for (int i = 0; i < LookupTables.RegularCellData.Length; i++)
        {
            regularCellDataTrisBeforeCurrent[i] = regularCellTotalVerts;

            regularCellTotalVerts += (int)LookupTables.RegularCellData[i].vertexIndex.Length;
        }

        for (int i = 0; i < LookupTables.RegularCellClass.Length; i++)
        {
            regularVertexDataVerticesBeforeCurrent[i] = regularVertexDataTotalVerts;

            regularVertexDataTotalVerts += LookupTables.RegularVertexData[i].Length;
        }

        regularCellClass = new NativeArray<byte>(256, Allocator.Persistent);
        regularCellDataCount = new NativeArray<byte>(16, Allocator.Persistent);
        regularVertexData = new NativeArray<ushort>(regularVertexDataTotalVerts, Allocator.Persistent);
        regularCellDataTris = new NativeArray<byte>(regularCellTotalVerts, Allocator.Persistent);
        regularCornerIndex = new NativeArray<int3>(LookupTables.CornerIndex.Length, Allocator.Persistent);

        for (int i = 0; i < LookupTables.CornerIndex.Length; i++)
        {
            regularCornerIndex[i] = LookupTables.CornerIndex[i];
        }

        for (int i = 0; i < LookupTables.RegularCellClass.Length; i++)
        {
            regularCellClass[i] = LookupTables.RegularCellClass[i];

            for (int j = 0; j < LookupTables.RegularVertexData[i].Length; j++)
            {
                regularVertexData[regularVertexDataVerticesBeforeCurrent[i] + j] = LookupTables.RegularVertexData[i][j];
            }
        }

        for (int i = 0; i < LookupTables.RegularCellData.Length; i++)
        {
            regularCellDataCount[i] = LookupTables.RegularCellData[i].geometryCounts;

            for (int j = 0; j < LookupTables.RegularCellData[i].vertexIndex.Length; j++)
            {
                regularCellDataTris[regularCellDataTrisBeforeCurrent[i] + j] = LookupTables.RegularCellData[i].vertexIndex[j];
            }
        }
    }

    void initTransCellData()
    {
        transVertexDataVerticesBeforeCurrent = new NativeArray<int>(LookupTables.TransitionCellClass.Length, Allocator.Persistent);
        transCellDataTrisBeforeCurrent = new NativeArray<int>(LookupTables.TransitionRegularCellData.Length, Allocator.Persistent);

        int transCellTotalVerts = 0;
        int transVertexDataTotalVerts = 0;

        for (int i = 0; i < LookupTables.TransitionRegularCellData.Length; i++)
        {
            transCellDataTrisBeforeCurrent[i] = transCellTotalVerts;

            transCellTotalVerts += (int)LookupTables.TransitionRegularCellData[i].vertexIndex.Length;
        }

        for (int i = 0; i < LookupTables.TransitionCellClass.Length; i++)
        {
            transVertexDataVerticesBeforeCurrent[i] = transVertexDataTotalVerts;

            transVertexDataTotalVerts += LookupTables.TransitionVertexData[i].Length;
        }

        transCellClass = new NativeArray<byte>(LookupTables.TransitionCellClass.Length, Allocator.Persistent);
        transCellDataCount = new NativeArray<byte>(LookupTables.TransitionRegularCellData.Length, Allocator.Persistent);
        transVertexData = new NativeArray<ushort>(transVertexDataTotalVerts, Allocator.Persistent);
        transCellDataTris = new NativeArray<byte>(transCellTotalVerts, Allocator.Persistent);
        transCornerIndex = new NativeArray<int3>(LookupTables.CornerIndexTransitionCell.Length, Allocator.Persistent);

        for (int i = 0; i < LookupTables.CornerIndexTransitionCell.Length; i++)
        {
            transCornerIndex[i] = LookupTables.CornerIndexTransitionCell[i];
        }

        for (int i = 0; i < LookupTables.TransitionCellClass.Length; i++)
        {
            transCellClass[i] = LookupTables.TransitionCellClass[i];

            for (int j = 0; j < LookupTables.TransitionVertexData[i].Length; j++)
            {
                transVertexData[transVertexDataVerticesBeforeCurrent[i] + j] = LookupTables.TransitionVertexData[i][j];
            }
        }

        for (int i = 0; i < LookupTables.TransitionRegularCellData.Length; i++)
        {
            transCellDataCount[i] = LookupTables.TransitionRegularCellData[i].geometryCounts;

            for (int j = 0; j < LookupTables.TransitionRegularCellData[i].vertexIndex.Length; j++)
            {
                transCellDataTris[transCellDataTrisBeforeCurrent[i] + j] = LookupTables.TransitionRegularCellData[i].vertexIndex[j];
            }
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
