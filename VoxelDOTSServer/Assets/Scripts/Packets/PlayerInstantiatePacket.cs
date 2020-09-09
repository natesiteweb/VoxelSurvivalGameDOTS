using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LiteNetLib;
using LiteNetLib.Utils;

public class PlayerInstantiatePacket
{
    public int id { get; set; }
    public Vector3Serializable position { get; set; }
    public QuaternionSerializable rotation { get; set; }
}

public struct Vector3Serializable : INetSerializable
{
    public float x;
    public float y;
    public float z;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(x);
        writer.Put(y);
        writer.Put(z);
    }

    public void Deserialize(NetDataReader reader)
    {
        x = reader.GetFloat();
        y = reader.GetFloat();
        z = reader.GetFloat();
    }
}

public struct QuaternionSerializable : INetSerializable
{
    public float w;
    public float x;
    public float y;
    public float z;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(w);
        writer.Put(x);
        writer.Put(y);
        writer.Put(z);
    }

    public void Deserialize(NetDataReader reader)
    {
        w = reader.GetFloat();
        x = reader.GetFloat();
        y = reader.GetFloat();
        z = reader.GetFloat();
    }
}

public class TransformConverter
{
    static Vector3 pos = new Vector3();
    static Quaternion rot = new Quaternion();

    static Vector3Serializable pos2 = new Vector3Serializable();
    static QuaternionSerializable rot2 = new QuaternionSerializable();

    public static Vector3 ToVector3(Vector3Serializable p)
    {
        pos.x = p.x;
        pos.y = p.y;
        pos.z = p.z;

        return pos;
    }

    public static Quaternion ToQuaternion(QuaternionSerializable r)
    {
        rot.w = r.w;
        rot.x = r.x;
        rot.y = r.y;
        rot.z = r.z;

        return rot;
    }

    public static Vector3Serializable ToVector3Serializable(Vector3 p)
    {
        pos2.x = p.x;
        pos2.y = p.y;
        pos2.z = p.z;

        return pos2;
    }

    public static QuaternionSerializable ToQuaternionSerializable(Quaternion r)
    {
        rot2.w = r.w;
        rot2.x = r.x;
        rot2.y = r.y;
        rot2.z = r.z;

        return rot2;
    }
}