using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;

public class TransChunk : MonoBehaviour
{
    public float[] densities;
    public int3 dimension = new int3(1);

    Mesh mesh;

    void Awake()
    {
        mesh = new Mesh();

        GetComponent<MeshFilter>().mesh = mesh;
    }
}
