using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class TransvoxelConversion : MonoBehaviour
{
    public InputField inputA;
    public InputField inputB;
    public Text outputEdge;

    public void ConvertVertexToEdge()
    {
        int vert0 = int.Parse(inputA.text);
        int vert1 = int.Parse(inputB.text);
    }
}
