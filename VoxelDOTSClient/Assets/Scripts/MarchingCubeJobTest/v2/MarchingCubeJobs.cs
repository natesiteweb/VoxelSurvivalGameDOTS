using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Burst;

public class MarchingCubeJobs
{
    static int3 to3D(int id, int3 gridDim)
    {
        int3 res;
        res.x = id / (gridDim.y * gridDim.z); //Note the integer division . This is x
        res.y = (id - res.x * gridDim.y * gridDim.z) / gridDim.z; //This is y
        res.z = id - res.x * gridDim.y * gridDim.z - res.y * gridDim.z; //This is z
        return res;
    }

    static int to1D(int3 ids, int3 dim)
    {
        return (dim.y * dim.z * ids.x) + (dim.z * ids.y) + ids.z;
    }

    static int btoi(bool v)
    {
        if (v)
            return 1;
        return 0;
    }

    [BurstCompile]
    public struct CountVertexPerVoxelJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float> densV;
        [ReadOnly] public NativeArray<int> nbTriTable;
        [ReadOnly] public NativeArray<int> triTable;
        public NativeArray<uint2> vertPerCell;
        //[ReadOnly] public int3 gridSize;
        [ReadOnly] public int3 chunkSize;
        [ReadOnly] public int3 normalOffset;
        [ReadOnly] public int totalVoxel;
        [ReadOnly] public bool isBorderChunk;
        public float isoValue;

        void IJobParallelFor.Execute(int index)
        {
            uint2 Nverts;
            Nverts.x = 0;
            Nverts.y = 0;
            int3 ijk = to3D(index, chunkSize);

            if(!isBorderChunk)
            {
                if (ijk.x > (chunkSize.x - (normalOffset.x + 2)) || ijk.y > (chunkSize.y - (normalOffset.y + 2)) || ijk.z > (chunkSize.z - (normalOffset.z + 2)) || ijk.x < normalOffset.x || ijk.y < normalOffset.y || ijk.z < normalOffset.z)
                {
                    vertPerCell[index] = Nverts;
                    return;
                }
            }
            else
            {
                if (ijk.x > (chunkSize.x - (normalOffset.x + 2)) || ijk.y > (chunkSize.y - (normalOffset.y + 2)) || ijk.z > (chunkSize.z - (normalOffset.z + 2)) || ijk.x < normalOffset.x || ijk.y < normalOffset.y || ijk.z < normalOffset.z)
                {
                    if(ijk.x > (chunkSize.x - (2)) || ijk.y > (chunkSize.y - (2)) || ijk.z > (chunkSize.z - (2)) || ijk.x < 0 || ijk.y < 0 || ijk.z < 0)
                    {
                        vertPerCell[index] = Nverts;
                        return;
                    }
                }
                else
                {
                    vertPerCell[index] = Nverts;
                    return;
                }
            }

            float voxel0 = densV[to1D(ijk, chunkSize)];
            float voxel1 = densV[to1D(ijk + new int3(1, 0, 0), chunkSize)];
            float voxel2 = densV[to1D(ijk + new int3(1, 1, 0), chunkSize)];
            float voxel3 = densV[to1D(ijk + new int3(0, 1, 0), chunkSize)];
            float voxel4 = densV[to1D(ijk + new int3(0, 0, 1), chunkSize)];
            float voxel5 = densV[to1D(ijk + new int3(1, 0, 1), chunkSize)];
            float voxel6 = densV[to1D(ijk + new int3(1, 1, 1), chunkSize)];
            float voxel7 = densV[to1D(ijk + new int3(0, 1, 1), chunkSize)];

            int cubeIndex = btoi(voxel0 < isoValue);
            cubeIndex += (btoi(voxel1 < isoValue)) * 2;
            cubeIndex += (btoi(voxel2 < isoValue)) * 4;
            cubeIndex += (btoi(voxel3 < isoValue)) * 8;
            cubeIndex += (btoi(voxel4 < isoValue)) * 16;
            cubeIndex += (btoi(voxel5 < isoValue)) * 32;
            cubeIndex += (btoi(voxel6 < isoValue)) * 64;
            cubeIndex += (btoi(voxel7 < isoValue)) * 128;

            uint nbTri = (uint)nbTriTable[cubeIndex];

            Nverts.x = nbTri;
            Nverts.y = (uint)btoi(nbTri > 0);
            vertPerCell[index] = Nverts;
        }

    }

    [BurstCompile]
    public struct ExclusiveScanTrivialJob : IJob
    {
        public NativeArray<uint2> vertPerCell;
        public NativeArray<uint2> result;
        [ReadOnly] public int totalVoxel;

        void IJob.Execute()
        {
            for (int i = 1; i < totalVoxel; i++)
            {
                result[i] = vertPerCell[i - 1] + result[i - 1];
            }
            result[0] = new uint2(0, 0);
        }
    }

    [BurstCompile]
    public struct CompactVoxelJob : IJobParallelFor
    {
        [NativeDisableParallelForRestriction]
        public NativeArray<uint> compVoxel;
        [ReadOnly] public NativeArray<uint2> vertPerCell;
        //[ReadOnly] public int3 gridSize;
        //[ReadOnly] public int chunkSize;
        [ReadOnly] public int totalVoxel;
        [ReadOnly] public uint lastElem;

        void IJobParallelFor.Execute(int index)
        {

            if ((index < totalVoxel - 1) ? vertPerCell[index].y < vertPerCell[index + 1].y : lastElem > 0)
            {
                compVoxel[(int)vertPerCell[index].y] = (uint)index;
            }
        }
    }

    [BurstCompile]
    public struct MarchingCubesJob : IJobParallelFor
    {

        [NativeDisableParallelForRestriction]
        public NativeArray<float3> vertices;
        [ReadOnly] public NativeArray<uint> compVoxel;
        [ReadOnly] public NativeArray<uint2> vertPerCell;
        [ReadOnly] public NativeArray<float> densV;
        [ReadOnly] public NativeArray<int> nbTriTable;
        [ReadOnly] public NativeArray<int> triTable;
        [ReadOnly] public float3 oriGrid;
        [ReadOnly] public float dx;
        //[ReadOnly] public int3 gridSize;
        [ReadOnly] public int3 chunkSize;
        [ReadOnly] public int3 normalOffset;
        [ReadOnly] public float isoValue;
        [ReadOnly] public float totalVerts;
        [ReadOnly] public float vertexScale;

        void IJobParallelFor.Execute(int index)
        {
            int voxel = (int)compVoxel[index];
            int3 ijk = to3D(voxel, chunkSize);
            float3 zer = float3.zero;
            float3 p = gridPosition(ijk, oriGrid, dx); //This just converts the to3D result to float3
            float3 offs = new float3(dx, 0, 0);

            float3 v0 = p;

            float3 v1 = p + offs;

            offs.x = dx;
            offs.y = dx;
            offs.z = 0.0f;

            float3 v2 = p + offs;

            offs.x = 0.0f;
            offs.y = dx;
            offs.z = 0.0f;

            float3 v3 = p + offs;

            offs.x = 0.0f;
            offs.y = 0.0f;
            offs.z = dx;

            float3 v4 = p + offs;

            offs.x = dx;
            offs.y = 0.0f;
            offs.z = dx;

            float3 v5 = p + offs;

            offs.x = dx;
            offs.y = dx;
            offs.z = dx;

            float3 v6 = p + offs;

            offs.x = 0.0f;
            offs.y = dx;
            offs.z = dx;

            float3 v7 = p + offs;

            float voxel0 = densV[to1D(ijk, chunkSize)];
            float voxel1 = densV[to1D(ijk + new int3(1, 0, 0), chunkSize)];
            float voxel2 = densV[to1D(ijk + new int3(1, 1, 0), chunkSize)];
            float voxel3 = densV[to1D(ijk + new int3(0, 1, 0), chunkSize)];
            float voxel4 = densV[to1D(ijk + new int3(0, 0, 1), chunkSize)];
            float voxel5 = densV[to1D(ijk + new int3(1, 0, 1), chunkSize)];
            float voxel6 = densV[to1D(ijk + new int3(1, 1, 1), chunkSize)];
            float voxel7 = densV[to1D(ijk + new int3(0, 1, 1), chunkSize)];

            int cubeIndex = btoi(voxel0 < isoValue);
            cubeIndex += (btoi(voxel1 < isoValue)) * 2;
            cubeIndex += (btoi(voxel2 < isoValue)) * 4;
            cubeIndex += (btoi(voxel3 < isoValue)) * 8;
            cubeIndex += (btoi(voxel4 < isoValue)) * 16;
            cubeIndex += (btoi(voxel5 < isoValue)) * 32;
            cubeIndex += (btoi(voxel6 < isoValue)) * 64;
            cubeIndex += (btoi(voxel7 < isoValue)) * 128;

            float3 verts0 = vertexInterp(isoValue, v0, v1, voxel0, voxel1) - normalOffset;
            float3 verts1 = vertexInterp(isoValue, v1, v2, voxel1, voxel2) - normalOffset;
            float3 verts2 = vertexInterp(isoValue, v2, v3, voxel2, voxel3) - normalOffset;
            float3 verts3 = vertexInterp(isoValue, v3, v0, voxel3, voxel0) - normalOffset;
            float3 verts4 = vertexInterp(isoValue, v4, v5, voxel4, voxel5) - normalOffset;
            float3 verts5 = vertexInterp(isoValue, v5, v6, voxel5, voxel6) - normalOffset;
            float3 verts6 = vertexInterp(isoValue, v6, v7, voxel6, voxel7) - normalOffset;
            float3 verts7 = vertexInterp(isoValue, v7, v4, voxel7, voxel4) - normalOffset;
            float3 verts8 = vertexInterp(isoValue, v0, v4, voxel0, voxel4) - normalOffset;
            float3 verts9 = vertexInterp(isoValue, v1, v5, voxel1, voxel5) - normalOffset;
            float3 verts10 = vertexInterp(isoValue, v2, v6, voxel2, voxel6) - normalOffset;
            float3 verts11 = vertexInterp(isoValue, v3, v7, voxel3, voxel7) - normalOffset;

            int numVerts = nbTriTable[cubeIndex];

            for (int i = 0; i < numVerts; i += 3)
            {

                int id = (int)vertPerCell[(int)voxel].x + i;
                if (id >= totalVerts)
                    return;
                int edge = triTable[i + cubeIndex * 16]; // ==> triTable[cubeIndex][i]

                //Avoid using an array by doing a lot of if...
                //TODO: improve that part
                if (edge == 0) vertices[id] = verts0 * vertexScale;
                else if (edge == 1) vertices[id] = verts1 * vertexScale;
                else if (edge == 2) vertices[id] = verts2 * vertexScale;
                else if (edge == 3) vertices[id] = verts3 * vertexScale;
                else if (edge == 4) vertices[id] = verts4 * vertexScale;
                else if (edge == 5) vertices[id] = verts5 * vertexScale;
                else if (edge == 6) vertices[id] = verts6 * vertexScale;
                else if (edge == 7) vertices[id] = verts7 * vertexScale;
                else if (edge == 8) vertices[id] = verts8 * vertexScale;
                else if (edge == 9) vertices[id] = verts9 * vertexScale;
                else if (edge == 10) vertices[id] = verts10 * vertexScale;
                else if (edge == 11) vertices[id] = verts11 * vertexScale;

                edge = triTable[(i + 1) + cubeIndex * 16];
                if (edge == 0) vertices[id + 1] = verts0 * vertexScale;
                else if (edge == 1) vertices[id + 1] = verts1 * vertexScale;
                else if (edge == 2) vertices[id + 1] = verts2 * vertexScale;
                else if (edge == 3) vertices[id + 1] = verts3 * vertexScale;
                else if (edge == 4) vertices[id + 1] = verts4 * vertexScale;
                else if (edge == 5) vertices[id + 1] = verts5 * vertexScale;
                else if (edge == 6) vertices[id + 1] = verts6 * vertexScale;
                else if (edge == 7) vertices[id + 1] = verts7 * vertexScale;
                else if (edge == 8) vertices[id + 1] = verts8 * vertexScale;
                else if (edge == 9) vertices[id + 1] = verts9 * vertexScale;
                else if (edge == 10) vertices[id + 1] = verts10 * vertexScale;
                else if (edge == 11) vertices[id + 1] = verts11 * vertexScale;

                edge = triTable[(i + 2) + cubeIndex * 16];
                if (edge == 0) vertices[id + 2] = verts0 * vertexScale;
                else if (edge == 1) vertices[id + 2] = verts1 * vertexScale;
                else if (edge == 2) vertices[id + 2] = verts2 * vertexScale;
                else if (edge == 3) vertices[id + 2] = verts3 * vertexScale;
                else if (edge == 4) vertices[id + 2] = verts4 * vertexScale;
                else if (edge == 5) vertices[id + 2] = verts5 * vertexScale;
                else if (edge == 6) vertices[id + 2] = verts6 * vertexScale;
                else if (edge == 7) vertices[id + 2] = verts7 * vertexScale;
                else if (edge == 8) vertices[id + 2] = verts8 * vertexScale;
                else if (edge == 9) vertices[id + 2] = verts9 * vertexScale;
                else if (edge == 10) vertices[id + 2] = verts10 * vertexScale;
                else if (edge == 11) vertices[id + 2] = verts11 * vertexScale;
            }
        }

        //For my needs this only converts int3 cellPos to float3
        float3 gridPosition(int3 cellPos, float3 originGrid, float dx)
        {
            float3 cp = new float3(cellPos.x, cellPos.y, cellPos.z);
            return (originGrid + (cp * dx));
        }

        float3 vertexInterp(float iso, float3 p0, float3 p1, float f0, float f1)
        {
            float t = (iso - f0) / (f1 - f0);
            return math.lerp(p0, p1, t);
        }
    }

    [BurstCompile]
    public struct ComputeNormalsJob : IJobParallelFor
    {
        [NativeDisableParallelForRestriction]
        public NativeArray<float3> normals;

        [ReadOnly] public NativeArray<float3> vertices;
        [ReadOnly] public NativeArray<float> densV;
        [ReadOnly] public float3 oriGrid;
        [ReadOnly] public float dx;
        //[ReadOnly] public int3 gridSize;
        [ReadOnly] public int3 chunkSize;
        [ReadOnly] public float vertexScale;
        [ReadOnly] public bool isTrans;
        [ReadOnly] public int3 normalOffset;

        void IJobParallelFor.Execute(int index)
        {
            float3 v = vertices[index];
            int3 ijk = (int3)((v - oriGrid) / dx / vertexScale) + normalOffset;

            int id = to1D(ijk, chunkSize);
            float field0 = densV[id];
            float field1 = densV[id];
            float field2 = densV[id];
            float field3 = densV[id];
            float field4 = densV[id];
            float field5 = densV[id];

            if (ijk.x < chunkSize.x - 1)
                field0 = densV[to1D(ijk + new int3(1, 0, 0), chunkSize)];
            if (ijk.x > 0)
                field1 = densV[to1D(ijk - new int3(1, 0, 0), chunkSize)];
            if (ijk.y < chunkSize.y - 1)
                field2 = densV[to1D(ijk + new int3(0, 1, 0), chunkSize)];
            if (ijk.y > 0)
                field3 = densV[to1D(ijk - new int3(0, 1, 0), chunkSize)];
            if (ijk.z < chunkSize.z - 1)
                field4 = densV[to1D(ijk + new int3(0, 0, 1), chunkSize)];
            if (ijk.z > 0)
                field5 = densV[to1D(ijk - new int3(0, 0, 1), chunkSize)];

            float3 n;
            n.x = field1 - field0;
            n.y = field3 - field2;
            n.z = field5 - field4;

            n.y *= -1f;
            n.z *= -1f;
            n.x *= -1f;

            float length = math.sqrt(n.x * n.x + n.y * n.y + n.z * n.z);

            normals[index] = n / length;

        }
    }

    [BurstCompile]
    public struct ComputeColorsJobTEMP : IJobParallelFor
    {
        [NativeDisableParallelForRestriction]
        public NativeArray<Color> colors;

        [ReadOnly] public NativeArray<float3> vertices;
        [ReadOnly] public NativeArray<float> densV;
        [ReadOnly] public float radius;
        [ReadOnly] public int LODActualSize;
        [ReadOnly] public int actualChunkSize;
        [ReadOnly] public int baseChunkSize;
        [ReadOnly] public int lod;
        [ReadOnly] public float3 chunkPos;
        [ReadOnly] public float3 oriGrid;
        [ReadOnly] public float dx;
        //[ReadOnly] public int3 gridSize;
        [ReadOnly] public int chunkSize;
        [ReadOnly] public float vertexScale;

        void IJobParallelFor.Execute(int index)
        {

            float3 v = vertices[index];
            int3 ijk = (int3)((v - oriGrid) / dx / vertexScale);
            int3 ijk2 = (int3)((v - oriGrid) / dx);

            /*int id = to1D(ijk, chunkSize);
            float field0 = densV[id];
            float field1 = densV[id];
            float field2 = densV[id];
            float field3 = densV[id];
            float field4 = densV[id];
            float field5 = densV[id];

            if (ijk.x < chunkSize - 1)
                field0 = densV[to1D(ijk + new int3(1, 0, 0), chunkSize)];
            if (ijk.x > 0)
                field1 = densV[to1D(ijk - new int3(1, 0, 0), chunkSize)];
            if (ijk.y < chunkSize - 1)
                field2 = densV[to1D(ijk + new int3(0, 1, 0), chunkSize)];
            if (ijk.y > 0)
                field3 = densV[to1D(ijk - new int3(0, 1, 0), chunkSize)];
            if (ijk.z < chunkSize - 1)
                field4 = densV[to1D(ijk + new int3(0, 0, 1), chunkSize)];
            if (ijk.z > 0)
                field5 = densV[to1D(ijk - new int3(0, 0, 1), chunkSize)];*/

            float posModifier = (LODActualSize / 2f);
            float scaleFactor = actualChunkSize / baseChunkSize;

            float x = ijk.x * scaleFactor - posModifier + chunkPos.x;
            float y = ijk.y * scaleFactor - posModifier + chunkPos.y;
            float z = ijk.z * scaleFactor - posModifier + chunkPos.z;

            Color n;

            float distance = math.sqrt((x * x) + (y * y) + (z * z));

            if (distance > radius + 15f && lod < 11)
                n = Color.red;

            n = new Color(math.clamp(((distance - (radius + 500f)) / 50f), 0f, 1f), 0f, 0f);

            if ((distance < radius + 2f))
                n = Color.green;

            if (lod >= 11)
                n = Color.green;

            colors[index] = n;

        }
    }

    [BurstCompile]
    public struct ComputeTerrainDataJob : IJobParallelFor
    {
        [NativeDisableParallelForRestriction]
        public NativeArray<float> densV;

        [ReadOnly] public float radius;
        [ReadOnly] public float scaleFactor;
        [ReadOnly] public int LODActualSize;
        [ReadOnly] public int3 baseChunkSize;
        [ReadOnly] public int3 normalOffset;
        [ReadOnly] public int totalSize;
        [ReadOnly] public float3 chunkPos;
        [ReadOnly] public float isoValue;

        void IJobParallelFor.Execute(int index)
        {
            int3 ijk = to3D(index, baseChunkSize) - normalOffset;

            float posModifier = (LODActualSize / 2f);

            float x = ijk.x * scaleFactor - posModifier + chunkPos.x;
            float y = ijk.y * scaleFactor - posModifier + chunkPos.y;
            float z = ijk.z * scaleFactor - posModifier + chunkPos.z;

            float3 point = new float3(x, y, z);
            float3 sP = surfacePosition(point, radius);
            float3 point2 = point - sP;

            float noiseValue1 = math.min(0f, noise.snoise((sP) * (0.0003f)) * 100f);

            float noise2 = noise.snoise((sP) * (0.03f));

            float noiseValue2 = math.min(0f, noise2 * 1f);
            noiseValue2 += math.max(0f, noise2 * 1f);

            float thisHeight = ((x * x + y * y + z * z) - (radius * radius));// + math.min(0, noise.snoise((new float3(x, y, z)) * (0.0003f))) * 1000000000f;// - noise.snoise((new float3(x, y, z)) * (0.05f)) * 3000000f;

            thisHeight /= 10000000f;

            densV[to1D(ijk + normalOffset, baseChunkSize)] = thisHeight + noiseValue1 + noiseValue2;
        }

        static float3 surfacePosition(float3 worldPos, float radius)
        {
            return math.normalize(worldPos) * radius;
        }
    }

    [BurstCompile]
    public struct MarchingCubesJobLengyel : IJobParallelFor
    {

        [NativeDisableParallelForRestriction]
        public NativeArray<float3> vertices;
        [ReadOnly] public NativeArray<uint> compVoxel;
        [ReadOnly] public NativeArray<uint2> vertPerCell;
        [ReadOnly] public NativeArray<float> densV;

        [ReadOnly] public NativeArray<byte> regularCellClass;
        [ReadOnly] public NativeArray<byte> regularCellDataCount;
        [ReadOnly] public NativeArray<byte> regularCellDataTris;
        [ReadOnly] public NativeArray<ushort> regularVertexData;
        [ReadOnly] public NativeArray<int> regularVertexDataVerticesBeforeCurrent;
        [ReadOnly] public NativeArray<int> regularCellDataTrisBeforeCurrent;
        [ReadOnly] public NativeArray<int3> CornerIndex;

        [ReadOnly] public float3 oriGrid;
        [ReadOnly] public float dx;
        //[ReadOnly] public int3 gridSize;
        [ReadOnly] public int3 chunkSize;
        [ReadOnly] public float isoValue;
        [ReadOnly] public float totalVerts;
        [ReadOnly] public float vertexScale;
        [ReadOnly] public byte sideConfig;

        void IJobParallelFor.Execute(int index)
        {
            int voxel = (int)compVoxel[index];
            int3 ijk = to3D(voxel, chunkSize);
            float3 zer = float3.zero;
            float3 p = gridPosition(ijk, oriGrid, dx); //This just converts the to3D result to float3

            float voxel0 = densV[to1D(ijk, chunkSize)];
            float voxel1 = densV[to1D(ijk + CornerIndex[1], chunkSize)];
            float voxel2 = densV[to1D(ijk + CornerIndex[2], chunkSize)];
            float voxel3 = densV[to1D(ijk + CornerIndex[3], chunkSize)];
            float voxel4 = densV[to1D(ijk + CornerIndex[4], chunkSize)];
            float voxel5 = densV[to1D(ijk + CornerIndex[5], chunkSize)];
            float voxel6 = densV[to1D(ijk + CornerIndex[6], chunkSize)];
            float voxel7 = densV[to1D(ijk + CornerIndex[7], chunkSize)];

            int cubeIndex = btoi(voxel0 < isoValue);
            cubeIndex += (btoi(voxel1 < isoValue)) * 2;
            cubeIndex += (btoi(voxel2 < isoValue)) * 4;
            cubeIndex += (btoi(voxel3 < isoValue)) * 8;
            cubeIndex += (btoi(voxel4 < isoValue)) * 16;
            cubeIndex += (btoi(voxel5 < isoValue)) * 32;
            cubeIndex += (btoi(voxel6 < isoValue)) * 64;
            cubeIndex += (btoi(voxel7 < isoValue)) * 128;

            int shapeCase = regularCellClass[cubeIndex];

            int numVerts = (regularCellDataCount[shapeCase] & 0x0F) * 3;

            for (int i = 0; i < numVerts; i += 3)
            {
                byte edge0 = (byte)(regularVertexData[regularVertexDataVerticesBeforeCurrent[cubeIndex] + regularCellDataTris[regularCellDataTrisBeforeCurrent[shapeCase] + i]] & 0x00FF);
                byte edge1 = (byte)(regularVertexData[regularVertexDataVerticesBeforeCurrent[cubeIndex] + regularCellDataTris[regularCellDataTrisBeforeCurrent[shapeCase] + i + 1]] & 0x00FF);
                byte edge2 = (byte)(regularVertexData[regularVertexDataVerticesBeforeCurrent[cubeIndex] + regularCellDataTris[regularCellDataTrisBeforeCurrent[shapeCase] + i + 2]] & 0x00FF);

                byte vert00 = (byte)((edge0 >> 4) & 0x0F);
                byte vert01 = (byte)(edge0 & 0x0F);

                byte vert10 = (byte)((edge1 >> 4) & 0x0F);
                byte vert11 = (byte)(edge1 & 0x0F);

                byte vert20 = (byte)((edge2 >> 4) & 0x0F);
                byte vert21 = (byte)(edge2 & 0x0F);

                int id = (int)vertPerCell[(int)voxel].x + i;

                if (id >= totalVerts)
                    return;

                float3 vert0 = (vertexInterp(isoValue, p + new float3(CornerIndex[vert00]), p + new float3(CornerIndex[vert01]), densV[to1D(ijk + CornerIndex[vert00], chunkSize)], densV[to1D(ijk + CornerIndex[vert01], chunkSize)]) - new int3(1));
                float3 vert1 = (vertexInterp(isoValue, p + new float3(CornerIndex[vert10]), p + new float3(CornerIndex[vert11]), densV[to1D(ijk + CornerIndex[vert10], chunkSize)], densV[to1D(ijk + CornerIndex[vert11], chunkSize)]) - new int3(1));
                float3 vert2 = (vertexInterp(isoValue, p + new float3(CornerIndex[vert20]), p + new float3(CornerIndex[vert21]), densV[to1D(ijk + CornerIndex[vert20], chunkSize)], densV[to1D(ijk + CornerIndex[vert21], chunkSize)]) - new int3(1));

                float shiftAmount = .5f;

                //Left
                if (ijk.x < 2 && ((sideConfig >> 3) & 0x01) == 1)
                {
                    vert0.x = 1f - (1f - vert0.x) * shiftAmount;
                    vert1.x = 1f - (1f - vert1.x) * shiftAmount;
                    vert2.x = 1f - (1f - vert2.x) * shiftAmount;
                }
                //Right
                else if (ijk.x > chunkSize.x - 4 && ((sideConfig >> 2) & 0x01) == 1)
                {
                    vert0.x = (chunkSize.x - 4f) + (vert0.x - (chunkSize.x - 4f)) * shiftAmount;
                    vert1.x = (chunkSize.x - 4f) + (vert1.x - (chunkSize.x - 4f)) * shiftAmount;
                    vert2.x = (chunkSize.x - 4f) + (vert2.x - (chunkSize.x - 4f)) * shiftAmount;
                }
                //Bottom
                else if (ijk.y < 2 && ((sideConfig >> 2) & 0x01) == 1)
                {
                    vert0.y = 1f - (1f - vert0.y) * shiftAmount;
                    vert1.y = 1f - (1f - vert1.y) * shiftAmount;
                    vert2.y = 1f - (1f - vert2.y) * shiftAmount;
                }
                //Top
                else if (ijk.y > chunkSize.y - 4 && ((sideConfig) & 0x01) == 1)
                {
                    vert0.y = (chunkSize.y - 4f) + (vert0.y - (chunkSize.y - 4f)) * shiftAmount;
                    vert1.y = (chunkSize.y - 4f) + (vert1.y - (chunkSize.y - 4f)) * shiftAmount;
                    vert2.y = (chunkSize.y - 4f) + (vert2.y - (chunkSize.y - 4f)) * shiftAmount;
                }
                //Back
                else if (ijk.z < 2 && ((sideConfig >> 5) & 0x01) == 1)
                {
                    vert0.z = 1f - (1f - vert0.z) * shiftAmount;
                    vert1.z = 1f - (1f - vert1.z) * shiftAmount;
                    vert2.z = 1f - (1f - vert2.z) * shiftAmount;
                }
                //Front
                else if (ijk.z > chunkSize.z - 4 && ((sideConfig >> 4) & 0x01) == 1)
                {
                    vert0.z = (chunkSize.z - 4f) + (vert0.z - (chunkSize.z - 4f)) * shiftAmount;
                    vert1.z = (chunkSize.z - 4f) + (vert1.z - (chunkSize.z - 4f)) * shiftAmount;
                    vert2.z = (chunkSize.z - 4f) + (vert2.z - (chunkSize.z - 4f)) * shiftAmount;
                }

                vertices[id] = vert0 * vertexScale;
                vertices[id + 1] = vert1 * vertexScale;
                vertices[id + 2] = vert2 * vertexScale;
            }
        }

        //For my needs this only converts int3 cellPos to float3
        float3 gridPosition(int3 cellPos, float3 originGrid, float dx)
        {
            float3 cp = new float3(cellPos.x, cellPos.y, cellPos.z);
            return (originGrid + (cp * dx));
        }

        float3 vertexInterp(float iso, float3 p0, float3 p1, float f0, float f1)
        {
            float t = (iso - f0) / (f1 - f0);
            return math.lerp(p0, p1, t);
        }
    }

    [BurstCompile]
    public struct CountVertexPerVoxelJobLengyel : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float> densV;

        [ReadOnly] public NativeArray<byte> regularCellClass;
        [ReadOnly] public NativeArray<byte> regularCellDataCount;
        [ReadOnly] public NativeArray<int3> CornerIndex;

        public NativeArray<uint2> vertPerCell;
        //[ReadOnly] public int3 gridSize;
        [ReadOnly] public int3 chunkSize;
        [ReadOnly] public int totalVoxel;
        [ReadOnly] public float isoValue;

        void IJobParallelFor.Execute(int index)
        {
            uint2 Nverts;
            Nverts.x = 0;
            Nverts.y = 0;
            int3 ijk = to3D(index, chunkSize);

            if (ijk.x > (chunkSize.x - 3) || ijk.y > (chunkSize.y - 3) || ijk.z > (chunkSize.z - 3) || ijk.x < 1 || ijk.y < 1 || ijk.z < 1)
            {
                vertPerCell[index] = Nverts;
                return;
            }

            float voxel0 = densV[to1D(ijk, chunkSize)];
            float voxel1 = densV[to1D(ijk + CornerIndex[1], chunkSize)];
            float voxel2 = densV[to1D(ijk + CornerIndex[2], chunkSize)];
            float voxel3 = densV[to1D(ijk + CornerIndex[3], chunkSize)];
            float voxel4 = densV[to1D(ijk + CornerIndex[4], chunkSize)];
            float voxel5 = densV[to1D(ijk + CornerIndex[5], chunkSize)];
            float voxel6 = densV[to1D(ijk + CornerIndex[6], chunkSize)];
            float voxel7 = densV[to1D(ijk + CornerIndex[7], chunkSize)];

            int cubeIndex = btoi(voxel0 < isoValue);
            cubeIndex += (btoi(voxel1 < isoValue)) * 2;
            cubeIndex += (btoi(voxel2 < isoValue)) * 4;
            cubeIndex += (btoi(voxel3 < isoValue)) * 8;
            cubeIndex += (btoi(voxel4 < isoValue)) * 16;
            cubeIndex += (btoi(voxel5 < isoValue)) * 32;
            cubeIndex += (btoi(voxel6 < isoValue)) * 64;
            cubeIndex += (btoi(voxel7 < isoValue)) * 128;

            int shapeCase = regularCellClass[cubeIndex];

            uint nbTri = (byte)((regularCellDataCount[shapeCase] & 0x0F) * 3);

            Nverts.x = nbTri;
            Nverts.y = (uint)btoi(nbTri > 0);
            vertPerCell[index] = Nverts;
        }
    }

    [BurstCompile]
    public struct CountVertexPerVoxelTransJobLengyel : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float> densV;

        [ReadOnly] public NativeArray<byte> transCellClass;
        [ReadOnly] public NativeArray<byte> transCellDataCount;
        [ReadOnly] public NativeArray<int3> CornerIndexTransitionCell;

        public NativeArray<uint2> vertPerCell;
        //[ReadOnly] public int3 gridSize;
        [ReadOnly] public int3 chunkSize;
        [ReadOnly] public int totalVoxel;
        [ReadOnly] public int sideIndex;
        [ReadOnly] public int3 sideDirection;
        [ReadOnly] public int3 inverseSideDirection;
        public float isoValue;

        void IJobParallelFor.Execute(int index)
        {
            uint2 Nverts;
            Nverts.x = 0;
            Nverts.y = 0;
            int3 ijk = to3D(index, chunkSize);

            if (ijk.x * inverseSideDirection.x % 2 != 0 || ijk.y * inverseSideDirection.y % 2 != 0 || ijk.z * inverseSideDirection.z % 2 != 0)
            {
                vertPerCell[index] = Nverts;
                return;
            }

            if (ijk.x > (chunkSize.x - 4) * inverseSideDirection.x || ijk.y > (chunkSize.y - 4) * inverseSideDirection.y || ijk.z > (chunkSize.z - 4) * inverseSideDirection.z || ijk.x < 0 || ijk.y < 0 || ijk.z < 0)
            {
                vertPerCell[index] = Nverts;
                return;
            }

            int baseID = sideIndex * 13;

            NativeArray<float> voxels = new NativeArray<float>(13, Allocator.Temp);

            for (int i = 0; i < 13; i++)
            {
                if (i > 8)
                {
                    if (i == 9)
                        voxels[i] = voxels[0];
                    //cube[i] = cube[0];

                    if (i == 10)
                        voxels[i] = voxels[2];

                    if (i == 11)
                        voxels[i] = voxels[6];

                    if (i == 12)
                        voxels[i] = voxels[8];
                }
                else
                {
                    voxels[i] = densV[to1D(ijk + (CornerIndexTransitionCell[baseID + i] * inverseSideDirection) + (CornerIndexTransitionCell[baseID + i] * math.abs(sideDirection)), chunkSize)];
                }
            }

            //if(chunkSize.)

            int cubeIndex = 0; /*btoi(voxel0 < isoValue);
            cubeIndex += (btoi(voxel1 < isoValue)) * 2;
            cubeIndex += (btoi(voxel2 < isoValue)) * 4;
            cubeIndex += (btoi(voxel3 < isoValue)) * 8;
            cubeIndex += (btoi(voxel4 < isoValue)) * 16;
            cubeIndex += (btoi(voxel5 < isoValue)) * 32;
            cubeIndex += (btoi(voxel6 < isoValue)) * 64;
            cubeIndex += (btoi(voxel7 < isoValue)) * 128;*/

            if (voxels[0] >= isoValue)
            {
                cubeIndex |= 1 << 0;
            }

            if (voxels[1] >= isoValue)
            {
                cubeIndex |= 1 << 1;
            }

            if (voxels[2] >= isoValue)
            {
                cubeIndex |= 1 << 2;
            }

            if (voxels[3] >= isoValue)
            {
                cubeIndex |= 1 << 7;
            }

            if (voxels[4] >= isoValue)
            {
                cubeIndex |= 1 << 8;
            }

            if (voxels[5] >= isoValue)
            {
                cubeIndex |= 1 << 3;
            }

            if (voxels[6] >= isoValue)
            {
                cubeIndex |= 1 << 6;
            }

            if (voxels[7] >= isoValue)
            {
                cubeIndex |= 1 << 5;
            }

            if (voxels[8] >= isoValue)
            {
                cubeIndex |= 1 << 4;
            }

            cubeIndex = cubeIndex ^ 511;

            int shapeCase = transCellClass[cubeIndex] & 0x7F;

            uint nbTri = (byte)((transCellDataCount[shapeCase] & 0x0F) * 3);

            Nverts.x = nbTri;
            Nverts.y = (uint)btoi(nbTri > 0);
            vertPerCell[index] = Nverts;
        }
    }

    [BurstCompile]
    public struct MarchingCubesTransJobLengyel : IJobParallelFor
    {

        [NativeDisableParallelForRestriction]
        public NativeArray<float3> vertices;
        [ReadOnly] public NativeArray<uint> compVoxel;
        [ReadOnly] public NativeArray<uint2> vertPerCell;
        [ReadOnly] public NativeArray<float> densV;

        [ReadOnly] public NativeArray<byte> transCellClass;
        [ReadOnly] public NativeArray<byte> transCellDataCount;
        [ReadOnly] public NativeArray<byte> transCellDataTris;
        [ReadOnly] public NativeArray<ushort> transVertexData;
        [ReadOnly] public NativeArray<int> transVertexDataVerticesBeforeCurrent;
        [ReadOnly] public NativeArray<int> transCellDataTrisBeforeCurrent;
        [ReadOnly] public NativeArray<int3> CornerIndexTransitionCell;

        [ReadOnly] public float3 oriGrid;
        [ReadOnly] public float dx;
        //[ReadOnly] public int3 gridSize;
        [ReadOnly] public int3 chunkSize;
        [ReadOnly] public float isoValue;
        [ReadOnly] public float totalVerts;
        [ReadOnly] public float vertexScale;
        [ReadOnly] public int sideIndex;
        [ReadOnly] public int3 scale;
        [ReadOnly] public int3 inverseSideDirection;
        [ReadOnly] public int3 sideDirection;

        void IJobParallelFor.Execute(int index)
        {
            int voxel = (int)compVoxel[index];
            int3 ijk = to3D(voxel, chunkSize);
            float3 zer = float3.zero;
            float3 p = gridPosition(ijk, oriGrid, dx); //This just converts the to3D result to float3

            NativeArray<float> voxels = new NativeArray<float>(13, Allocator.Temp);

            int baseID = sideIndex * 13;

            for (int i = 0; i < 13; i++)
            {
                if (i > 8)
                {
                    if (i == 9)
                        voxels[i] = voxels[0];
                    //cube[i] = cube[0];

                    if (i == 10)
                        voxels[i] = voxels[2];

                    if (i == 11)
                        voxels[i] = voxels[6];

                    if (i == 12)
                        voxels[i] = voxels[8];
                }
                else
                {
                    voxels[i] = densV[to1D(ijk + (CornerIndexTransitionCell[baseID + i] * inverseSideDirection) + (CornerIndexTransitionCell[baseID + i] * math.abs(sideDirection)), chunkSize)];
                }
            }

            /*float voxel0 = densV[to1D(ijk, chunkSize)];
            float voxel1 = densV[to1D(ijk + CornerIndexTransitionCell[1], chunkSize)];
            float voxel2 = densV[to1D(ijk + CornerIndexTransitionCell[2], chunkSize)];
            float voxel3 = densV[to1D(ijk + CornerIndexTransitionCell[3], chunkSize)];
            float voxel4 = densV[to1D(ijk + CornerIndexTransitionCell[4], chunkSize)];
            float voxel5 = densV[to1D(ijk + CornerIndexTransitionCell[5], chunkSize)];
            float voxel6 = densV[to1D(ijk + CornerIndexTransitionCell[6], chunkSize)];
            float voxel7 = densV[to1D(ijk + CornerIndexTransitionCell[7], chunkSize)];
            float voxel8 = densV[to1D(ijk + CornerIndexTransitionCell[8], chunkSize)];
            float voxel9;// = densV[to1D(ijk + CornerIndexTransitionCell[9], chunkSize)];
            float voxel10;// = densV[to1D(ijk + CornerIndexTransitionCell[10], chunkSize)];
            float voxel11;// = densV[to1D(ijk + CornerIndexTransitionCell[11], chunkSize)];
            float voxel12;// = densV[to1D(ijk + CornerIndexTransitionCell[12], chunkSize)];*/

            //if(chunkSize.)

            int cubeIndex = 0; /*btoi(voxel0 < isoValue);
            cubeIndex += (btoi(voxel1 < isoValue)) * 2;
            cubeIndex += (btoi(voxel2 < isoValue)) * 4;
            cubeIndex += (btoi(voxel3 < isoValue)) * 8;
            cubeIndex += (btoi(voxel4 < isoValue)) * 16;
            cubeIndex += (btoi(voxel5 < isoValue)) * 32;
            cubeIndex += (btoi(voxel6 < isoValue)) * 64;
            cubeIndex += (btoi(voxel7 < isoValue)) * 128;*/

            if (voxels[0] >= isoValue)
            {
                cubeIndex |= 1 << 0;
            }

            if (voxels[1] >= isoValue)
            {
                cubeIndex |= 1 << 1;
            }

            if (voxels[2] >= isoValue)
            {
                cubeIndex |= 1 << 2;
            }

            if (voxels[3] >= isoValue)
            {
                cubeIndex |= 1 << 7;
            }

            if (voxels[4] >= isoValue)
            {
                cubeIndex |= 1 << 8;
            }

            if (voxels[5] >= isoValue)
            {
                cubeIndex |= 1 << 3;
            }

            if (voxels[6] >= isoValue)
            {
                cubeIndex |= 1 << 6;
            }

            if (voxels[7] >= isoValue)
            {
                cubeIndex |= 1 << 5;
            }

            if (voxels[8] >= isoValue)
            {
                cubeIndex |= 1 << 4;
            }

            cubeIndex = cubeIndex ^ 511;

            int shapeCase = transCellClass[cubeIndex] & 0x7F;

            int numVerts = (byte)((transCellDataCount[shapeCase] & 0x0F) * 3);

            bool inverse = (transCellClass[cubeIndex] & 128) != 0;

            for (int i = 0; i < numVerts; i += 3)
            {
                byte edge0 = (byte)(transVertexData[transVertexDataVerticesBeforeCurrent[cubeIndex] + transCellDataTris[transCellDataTrisBeforeCurrent[shapeCase] + i]] & 0x00FF);
                byte edge1 = (byte)(transVertexData[transVertexDataVerticesBeforeCurrent[cubeIndex] + transCellDataTris[transCellDataTrisBeforeCurrent[shapeCase] + i + 1]] & 0x00FF);
                byte edge2 = (byte)(transVertexData[transVertexDataVerticesBeforeCurrent[cubeIndex] + transCellDataTris[transCellDataTrisBeforeCurrent[shapeCase] + i + 2]] & 0x00FF);

                byte vert00 = (byte)((edge0 >> 4) & 0x0F);
                byte vert01 = (byte)(edge0 & 0x0F);

                byte vert10 = (byte)((edge1 >> 4) & 0x0F);
                byte vert11 = (byte)(edge1 & 0x0F);

                byte vert20 = (byte)((edge2 >> 4) & 0x0F);
                byte vert21 = (byte)(edge2 & 0x0F);

                int id = (int)vertPerCell[(int)voxel].x + i;

                if (id >= totalVerts)
                    return;

                float depthScale = 0.5f;

                float3 shrinkFactor = new float3(1f - (math.abs(scale.x) * depthScale), 1f - (math.abs(scale.y) * depthScale), 1f - (math.abs(scale.z) * depthScale));
                float3 shrinkFactor2 = float3.zero;

                if (scale.x < 1 && scale.y < 1 && scale.z < 1)
                {
                    shrinkFactor2 = new float3((math.abs(scale.x) * depthScale * 4f), (math.abs(scale.y) * depthScale * 4f), (math.abs(scale.z) * depthScale * 4f));
                }
                else
                {
                    shrinkFactor2 = new float3(-(math.abs(scale.x) * depthScale * 2f), -(math.abs(scale.y) * depthScale * 2f), -(math.abs(scale.z) * depthScale * 2f));
                }

                //shrinkFactor = new float3(1f);

                float3 vert0 = (vertexInterp(isoValue, p + new float3(CornerIndexTransitionCell[baseID + vert00]), p + new float3(CornerIndexTransitionCell[baseID + vert01]), voxels[vert00], voxels[vert01])) * shrinkFactor;
                float3 vert1 = (vertexInterp(isoValue, p + new float3(CornerIndexTransitionCell[baseID + vert10]), p + new float3(CornerIndexTransitionCell[baseID + vert11]), voxels[vert10], voxels[vert11])) * shrinkFactor;
                float3 vert2 = (vertexInterp(isoValue, p + new float3(CornerIndexTransitionCell[baseID + vert20]), p + new float3(CornerIndexTransitionCell[baseID + vert21]), voxels[vert20], voxels[vert21])) * shrinkFactor;

                if (!inverse)
                {
                    vertices[id + 0] = (vert0 + shrinkFactor2) * vertexScale;
                    vertices[id + 1] = (vert1 + shrinkFactor2) * vertexScale;
                    vertices[id + 2] = (vert2 + shrinkFactor2) * vertexScale;
                }
                else
                {
                    vertices[id + 2] = (vert0 + shrinkFactor2) * vertexScale;
                    vertices[id + 1] = (vert1 + shrinkFactor2) * vertexScale;
                    vertices[id + 0] = (vert2 + shrinkFactor2) * vertexScale;
                }


            }
        }

        //For my needs this only converts int3 cellPos to float3
        float3 gridPosition(int3 cellPos, float3 originGrid, float dx)
        {
            float3 cp = new float3(cellPos.x, cellPos.y, cellPos.z);
            return (originGrid + (cp * dx));
        }

        float3 vertexInterp(float iso, float3 p0, float3 p1, float f0, float f1)
        {
            float t;

            if (f1 - f0 == 0)
                t = isoValue;
            else
                t = (iso - f0) / (f1 - f0);

            //return math.lerp(p0, p1, t);
            return p0 + ((p1 - p0) * t);

            //return (p0 + p1) / 2f;
        }
    }
}