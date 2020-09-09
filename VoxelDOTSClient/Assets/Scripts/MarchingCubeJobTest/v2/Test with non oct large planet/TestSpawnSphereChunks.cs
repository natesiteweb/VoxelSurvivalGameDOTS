using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;

public class TestSpawnSphereChunks : MonoBehaviour
{
    [SerializeField]
    private float radius;
    [SerializeField]
    private float shellsize;
    [SerializeField]
    private int chunkSize;
    [SerializeField]
    private GameObject chunkPrefab;
    [SerializeField]
    private int chunkCount = 0;

    void Start()
    {
        for(int x = 0; x < chunkSize; x++)
        {
            for (int y = 0; y < chunkSize; y++)
            {
                for (int z = 0; z < chunkSize; z++)
                {
                    float value = (x * x + y * y + z * z) - radius*radius;

                    Bounds bounds = new Bounds(new float3(x, y, z) - new float3(chunkSize / 2f), Vector3.one);

                    float distance = Mathf.Sqrt(bounds.SqrDistance(Vector3.zero));

                    //distance = Vector3.Distance(Vector3.zero, new float3(x, y, z) - new float3(chunkSize / 2f));

                    if (distance < (radius) && distance > (radius) - shellsize)
                    {
                        Instantiate(chunkPrefab, new float3(x, y, z) - new float3(chunkSize / 2f), Quaternion.identity);
                        chunkCount++;
                    }
                }
            }
        }
    }

    void Update()
    {
        
    }
}
