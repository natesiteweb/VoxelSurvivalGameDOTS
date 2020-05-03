using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Jobs;

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

    static int to1D(int3 ids, int chunkSize)
    {
        return (chunkSize * chunkSize * ids.x) + (chunkSize * ids.y) + ids.z;
    }
    static int btoi(bool v)
    {
        if (v)
            return 1;
        return 0;
    }

    public struct CountVertexPerVoxelJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float> densV;
        [ReadOnly] public NativeArray<int> nbTriTable;
        [ReadOnly] public NativeArray<int> triTable;
        public NativeArray<uint2> vertPerCell;
        //[ReadOnly] public int3 gridSize;
        [ReadOnly] public int chunkSize;
        [ReadOnly] public int totalVoxel;
        public float isoValue;

        void IJobParallelFor.Execute(int index)
        {
            uint2 Nverts;
            Nverts.x = 0;
            Nverts.y = 0;
            int3 ijk = to3D(index, chunkSize);

            if (ijk.x > (chunkSize - 2) || ijk.y > (chunkSize - 2) || ijk.z > (chunkSize - 2))
            {
                vertPerCell[index] = Nverts;
                return;
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

            Nverts.x = (uint)nbTriTable[cubeIndex];
            Nverts.y = (uint)btoi(nbTriTable[cubeIndex] > 0);
            vertPerCell[index] = Nverts;
        }

    }

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


    public struct CompactVoxelJob : IJobParallelFor
    {
        [NativeDisableParallelForRestriction]
        public NativeArray<uint> compVoxel;
        [ReadOnly] public NativeArray<uint2> vertPerCell;
        //[ReadOnly] public int3 gridSize;
        [ReadOnly] public int chunkSize;
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
        [ReadOnly] public int chunkSize;
        [ReadOnly] public float isoValue;
        [ReadOnly] public float totalVerts;
        [ReadOnly] public float vertexScale;

        void IJobParallelFor.Execute(int index)
        {
            int voxel = (int)compVoxel[index];
            int3 ijk = to3D(voxel, chunkSize);
            float3 zer = float3.zero;
            float3 p = gridPosition(ijk, oriGrid, dx);
            float3 offs = new float3(dx, 0, 0);
            float3 v0 = p;
            float3 v1 = p + offs;
            offs.x = dx; offs.y = dx; offs.z = 0.0f;
            float3 v2 = p + offs;
            offs.x = 0.0f; offs.y = dx; offs.z = 0.0f;
            float3 v3 = p + offs;
            offs.x = 0.0f; offs.y = 0.0f; offs.z = dx;
            float3 v4 = p + offs;
            offs.x = dx; offs.y = 0.0f; offs.z = dx;
            float3 v5 = p + offs;
            offs.x = dx; offs.y = dx; offs.z = dx;
            float3 v6 = p + offs;
            offs.x = 0.0f; offs.y = dx; offs.z = dx;
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

            float3 verts0 = vertexInterp(isoValue, v0, v1, voxel0, voxel1);
            float3 verts1 = vertexInterp(isoValue, v1, v2, voxel1, voxel2);
            float3 verts2 = vertexInterp(isoValue, v2, v3, voxel2, voxel3);
            float3 verts3 = vertexInterp(isoValue, v3, v0, voxel3, voxel0);
            float3 verts4 = vertexInterp(isoValue, v4, v5, voxel4, voxel5);
            float3 verts5 = vertexInterp(isoValue, v5, v6, voxel5, voxel6);
            float3 verts6 = vertexInterp(isoValue, v6, v7, voxel6, voxel7);
            float3 verts7 = vertexInterp(isoValue, v7, v4, voxel7, voxel4);
            float3 verts8 = vertexInterp(isoValue, v0, v4, voxel0, voxel4);
            float3 verts9 = vertexInterp(isoValue, v1, v5, voxel1, voxel5);
            float3 verts10 = vertexInterp(isoValue, v2, v6, voxel2, voxel6);
            float3 verts11 = vertexInterp(isoValue, v3, v7, voxel3, voxel7);

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

    public struct ComputeNormalsJob : IJobParallelFor
    {
        [NativeDisableParallelForRestriction]
        public NativeArray<float3> normals;

        [ReadOnly] public NativeArray<float3> vertices;
        [ReadOnly] public NativeArray<float> densV;
        [ReadOnly] public float3 oriGrid;
        [ReadOnly] public float dx;
        //[ReadOnly] public int3 gridSize;
        [ReadOnly] public int chunkSize;
        [ReadOnly] public float vertexScale;

        void IJobParallelFor.Execute(int index)
        {

            float3 v = vertices[index];
            int3 ijk = (int3)((v - oriGrid) / dx / vertexScale);

            int id = to1D(ijk, chunkSize);
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
                field5 = densV[to1D(ijk - new int3(0, 0, 1), chunkSize)];

            float3 n;
            n.x = field1 - field0;
            n.y = field3 - field2;
            n.z = field5 - field4;

            n.y *= -1f;
            n.z *= -1f;

            normals[index] = n * vertexScale;

        }
    }

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

            if(lod >= 11)
                n = Color.green;

            colors[index] = n;

        }
    }
}