using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LiteNetLib;
using LiteNetLib.Utils;

public class PlayerTransformPacket
{
    public int id { get; set; }
    public Vector3 position { get; set; }
    public Quaternion rotation { get; set; }
}

public class PlayerInputPacket
{
    public int id { get; set; }
    public bool forward { get; set; }   //W
    public bool left { get; set; }      //A
    public bool backwards { get; set; } //S
    public bool right { get; set; }     //D
}