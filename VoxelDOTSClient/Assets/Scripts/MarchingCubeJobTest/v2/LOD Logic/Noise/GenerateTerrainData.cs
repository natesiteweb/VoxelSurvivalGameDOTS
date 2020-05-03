using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;

public class GenerateTerrainData : MonoBehaviour
{
    [SerializeField]
    private FastNoiseSIMDUnity[] noises;

    [SerializeField]
    private float[] multipliers;

    float[][] noiseValues;

    float posModifier;
    float scaleFactor;

    bool isFull;
    bool isEmpty;
    float thisHeight;

    float[] emptyReturnValue;

    Planet planet;

    public float[] Generate(Planet planet1, Chunk entity)
    {
        planet = planet1;

        //emptyReturnValue = new NativeArray<float>(planet.totalSize, Allocator.Temp);
        emptyReturnValue = new float[planet.totalSize];

        noiseValues = new float[noises.Length][];

        scaleFactor = entity.values.actualChunkSize / planet.baseChunkSize;

        for (int i = 0; i < noises.Length; i++)
        {
            noiseValues[i] = new float[planet.totalSize];
            noises[i].fastNoiseSIMD.SetAxisScales(scaleFactor, scaleFactor, scaleFactor);
            noiseValues[i] = noises[i].fastNoiseSIMD.GetNoiseSet(entity.values.chunkPos.x / (int)scaleFactor, entity.values.chunkPos.y / (int)scaleFactor, entity.values.chunkPos.z / (int)scaleFactor, planet.baseChunkSize + 1, planet.baseChunkSize + 1, planet.baseChunkSize + 1);
        }

        isFull = true;
        isEmpty = true;

        posModifier = (planet.LODActualSize / 2f);

        int id = 0;
        for (int i = 0; i < planet.baseChunkSize + 1; i++)
        {
            for (int j = 0; j < planet.baseChunkSize + 1; j++)
            {
                for (int k = 0; k < planet.baseChunkSize + 1; k++)
                {
                    float x = i * scaleFactor - posModifier + entity.values.chunkPos.x;
                    float y = j * scaleFactor - posModifier + entity.values.chunkPos.y;
                    float z = k * scaleFactor - posModifier + entity.values.chunkPos.z;

                    float noiseSum = 0f;


                    for(int n = 0; n < noises.Length; n++)
                    {
                        if (n == 0)
                        {
                            noiseSum += Mathf.Min(0, noiseValues[n][id] * multipliers[n]);
                        }
                        else
                        {
                            noiseSum += (noiseValues[n][id] * multipliers[n]);
                        }
                    }



                    //densVal[id++] = (x * x * x * x - 5.0f * x * x + y * y * y * y - 5.0f * y * y + z * z * z * z - 5.0f * z * z + 11.8f) * 0.2f + 0.5f;

                    //float thisHeight = 8f * Mathf.PerlinNoise((i + tempOffset.x) * .1f, (k + tempOffset.y) * .1f);
                    thisHeight = (x * x + y * y + z * z) - (planet.radius * planet.radius) + noiseSum;

                    emptyReturnValue[id] = thisHeight;
                    id++;

                    if (thisHeight < planet.isoValue)
                        isEmpty = false;

                    if (thisHeight > planet.isoValue)
                        isFull = false;
                }
            }
        }

        entity.values.isEmpty = isEmpty;
        entity.values.isFull = isFull;

        return emptyReturnValue;
    }

    float GetSurfaceNoise(Vector3 pos, Vector3 center, float noiseRadius, Chunk entity)
    {
        float3 surfacePosition = (pos - center).normalized * noiseRadius;

        return GetNoise(entity, surfacePosition);
    }

    float GetNoise(Chunk entity, float3 samplePos)
    {
        float[] noiseValues;

        scaleFactor = entity.values.actualChunkSize / planet.baseChunkSize;

        noiseValues = new float[planet.totalSize];
        noises[0].fastNoiseSIMD.SetAxisScales(scaleFactor, scaleFactor, scaleFactor);
        noiseValues = noises[0].fastNoiseSIMD.GetNoiseSet(entity.values.chunkPos.x / (int)scaleFactor, entity.values.chunkPos.y / (int)scaleFactor, entity.values.chunkPos.z / (int)scaleFactor, planet.baseChunkSize + 1, planet.baseChunkSize + 1, planet.baseChunkSize + 1);

        return noiseValues[to1D(new int3(Mathf.RoundToInt(samplePos.x), Mathf.RoundToInt(samplePos.y), Mathf.RoundToInt(samplePos.z)), entity.values.chunkSize)];
    }

    static int to1D(int3 ids, int chunkSize)
    {
        return (chunkSize * chunkSize * ids.x) + (chunkSize * ids.y) + ids.z;
    }
}
