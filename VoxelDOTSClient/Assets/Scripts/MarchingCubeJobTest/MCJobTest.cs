using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Transforms;
using Unity.Rendering;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Physics;


public class MCJobTest : MonoBehaviour
{
    [SerializeField]
    private Mesh mesh;

    [SerializeField]
    private UnityEngine.Material material;

    public bool moveBoxes = true;

    void Start()
    {


        EntityManager em = World.DefaultGameObjectInjectionWorld.EntityManager;

        EntityArchetype chunkArchetype = em.CreateArchetype(

            typeof(Translation),
            typeof(Rotation),
            typeof(Scale),
            typeof(RenderMesh),
            typeof(WorldRenderBounds),
            typeof(RenderBounds),
            typeof(LocalToWorld),

            typeof(ChunkComponent)
        );



        NativeArray<float> densities = new NativeArray<float>(35937, Allocator.TempJob);

        MCTestDensity testJob = new MCTestDensity
        {
            densities = densities
        };

        JobHandle jobHandle = testJob.Schedule(35937, 66);
        jobHandle.Complete();

        //NativeArray<float3> verts = new NativeArray<float3>(32768, Allocator.TempJob);
        //NativeArray<int> tris = new NativeArray<int>(11000, Allocator.TempJob);
        NativeArray<Triangle> tris = new NativeArray<Triangle>(163840, Allocator.TempJob);

        MCTestActual testJobMC = new MCTestActual
        {
            triIndex = 0,
            triangles = tris,
            densities = densities
        };

        JobHandle jobHandle2 = testJobMC.Schedule(32768, 64);
        jobHandle2.Complete();

        List<Vector3> vertices = new List<Vector3>();
        int[] triangles = new int[tris.Length * 3];

        Mesh newMesh = new Mesh();
        newMesh.Clear();

        for(int i = 0; i < tris.Length; i++)
        {
            for(int j = 0; j < 3; j++)
            {
                vertices.Add(new Vector3(tris[i][j].x, tris[i][j].y, tris[i][j].z));
                triangles[i * 3 + j] = i * 3 + j;
            }
        }

        newMesh.triangles = triangles;
        newMesh.vertices = vertices.ToArray();

        mesh.RecalculateNormals();

        NativeArray<Entity> entityArray = new NativeArray<Entity>(1, Allocator.Temp);
        em.CreateEntity(chunkArchetype, entityArray);

        for(int i = 0; i < entityArray.Length; i++)
        {
            Entity entity = entityArray[i];

            em.AddComponentData(entity, new LocalToWorld { });
            em.AddComponentData(entity, new Rotation { Value = quaternion.identity });
            em.SetComponentData(entity, new Translation { Value = float3.zero });
            em.SetSharedComponentData(entity, new RenderMesh { mesh = newMesh, material = material });
            em.SetComponentData(entity, new Scale { Value = .4f });
            em.SetComponentData(entity, new ChunkComponent { tempMoveSpeed = 2f, offset = float2.zero });
        }

        entityArray.Dispose();
        densities.Dispose();
        //verts.Dispose();
        tris.Dispose();
    }

    void Update()
    {
        
    }
}

public struct MCTestJob : IJobParallelFor
{
    //public NativeArray<float> densities;

    //[NativeDisableParallelForRestriction, WriteOnly] public NativeArray<float3> vertices;
    //[NativeDisableParallelForRestriction, WriteOnly] public NativeArray<int> triangles;
    public NativeArray<float3> positions;

    public void Execute(int index)
    {
        //float3 voxelLocation = new float3(index / (16 * 16), index / 16 % 16, index % 16);
        float3 voxelLocation = new float3(index % 100, index / (100 * 100), index / 100 % 100);

        positions[index] = voxelLocation;
    }
}

public struct MCTestJobMove : IJobParallelFor
{
    //public NativeArray<float> densities;

    //[NativeDisableParallelForRestriction, WriteOnly] public NativeArray<float3> vertices;
    //[NativeDisableParallelForRestriction, WriteOnly] public NativeArray<int> triangles;
    public NativeArray<float3> positions;
    public NativeArray<float> moveSpeeds;
    public float deltaTime;
    public float2 offset;

    public void Execute(int index)
    {
        float3 newLocation = positions[index];

        /*if (positions[index].x < -30f)
            moveSpeeds[index] = UnityEngine.Mathf.Abs(moveSpeeds[index]);
        else if (positions[index].x > 30f)
            moveSpeeds[index] = -UnityEngine.Mathf.Abs(moveSpeeds[index]);*/

        float2 voxelLocation = new float2(index % 100, index / 100 % 100);

        float voxelY = Mathf.PerlinNoise((voxelLocation.x + offset.x) * 0.1f, (voxelLocation.y + offset.y) * 0.1f);
    
        newLocation.y = voxelY * 10f;

        positions[index] = newLocation;
    }
}

public struct MCTestDensity : IJobParallelFor
{
    public NativeArray<float> densities;

    public void Execute(int index)
    {
        int3 voxelLocation = new int3(index / (33 * 33), index / 33 % 33, index % 33);

        float thisHeight = 20f * Mathf.PerlinNoise(voxelLocation.x * 0.1f, voxelLocation.z * 0.1f);
    }
}

public struct MCTestActual : IJobParallelFor
{
    //[NativeDisableParallelForRestriction, WriteOnly] public NativeArray<float3> vertices;
    //[NativeDisableParallelForRestriction, WriteOnly] public NativeArray<int> triangles;
    public NativeArray<Triangle> triangles;

    public NativeArray<float> densities;

    public int triIndex;

    public void Execute(int index)
    {
        //float3 voxelLocation = new float3(index / (33 * 33), index / 33 % 33, index % 33);

        float thisHeight = densities[index];

        float[] cube = new float[8];

        //x 1089
        //y 33
        //z 1

        cube[0] = densities[index];
        cube[1] = densities[index + 1089];
        cube[2] = densities[index + 1];
        cube[3] = densities[index + 1090];
        cube[4] = densities[index + 33];
        cube[5] = densities[index + 1122];
        cube[6] = densities[index + 34];
        cube[7] = densities[index + 1123];

        int configIndex = GetCubeConfiguration(cube);

        if (configIndex == 0 || configIndex == 255)
            return;

        int shapeCase = LookupTables.RegularCellClass[configIndex];
        int triCount = (int)LookupTables.RegularCellData[shapeCase].GetTriangleCount();
        int vertCount = (int)LookupTables.RegularCellData[shapeCase].GetVertexCount();
        ushort[] vertexLocations = LookupTables.RegularVertexData[configIndex];

        for(int i = 0; i < triCount; i++)
        {
            Triangle tri;

            int edge0 = LookupTables.RegularVertexData[configIndex][LookupTables.RegularVertexData[shapeCase][i * 3]] & 0x00FF;
            int edge1 = LookupTables.RegularVertexData[configIndex][LookupTables.RegularVertexData[shapeCase][i * 3 + 1]] & 0x00FF;
            int edge2 = LookupTables.RegularVertexData[configIndex][LookupTables.RegularVertexData[shapeCase][i * 3 + 2]] & 0x00FF;

            int vert00 = (edge0 >> 4) & 0x0F;
            int vert01 = edge0 & 0x0F;

            int vert10 = (edge1 >> 4) & 0x0F;
            int vert11 = edge1 & 0x0F;

            int vert20 = (edge2 >> 4) & 0x0F;
            int vert21 = edge2 & 0x0F;

            tri.vertexC = (vert00 + vert01) / 2f;
            tri.vertexB = (vert10 + vert11) / 2f;
            tri.vertexA = (vert20 + vert21) / 2f;

            triangles[triIndex] = (tri);

            triIndex += 1;
        }

        /*for(int i = 0; i < vertexLocations.Length; i++)
        {
            byte edge = (byte)(vertexLocations[i] & 0x00FF);

            byte vert1 = (byte)(edge >> 4);
            byte vert2 = (byte)(edge & 0x0F);

            float3 vertPos1 = new float3(LookupTables.CornerIndex[vert1].x, LookupTables.CornerIndex[vert1].y, LookupTables.CornerIndex[vert1].z);
            float3 vertPos2 = new float3(LookupTables.CornerIndex[vert2].x, LookupTables.CornerIndex[vert2].y, LookupTables.CornerIndex[vert2].z);

            float3 vertPos = (vertPos2 + vertPos1) / 2f;

        }*/
    }

    public int GetCubeConfiguration(float[] cube)
    {
        int configurationIndex = 0;
        for (int i = 0; i < 8; i++)
        {

            // If it is, use bit-magic to the set the corresponding bit to 1. So if only the 3rd point in the cube was below
            // the surface, the bit would look like 00100000, which represents the integer value 32.
            if (cube[i] > 0.5f)
                configurationIndex |= 1 << i;

        }

        return configurationIndex;
    }
}

public struct Triangle
{
    public float3 vertexA;
    public float3 vertexB;
    public float3 vertexC;

    public float3 this[int i]
    {
        get
        {
            switch (i)
            {
                case 0:
                    return vertexA;
                case 1:
                    return vertexB;
                default:
                    return vertexC;
            }
        }
    }
}