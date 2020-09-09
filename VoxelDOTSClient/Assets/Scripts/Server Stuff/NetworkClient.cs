using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LiteNetLib;
using LiteNetLib.Utils;
using System.Net;
using System.Net.Sockets;
using UnityEngine.UI;

public class NetworkClient : MonoBehaviour, INetEventListener
{
    public NetManager client;
    public NetPacketProcessor packetProcessor;

    public const int port = 904;
    public const string ip = "localhost";
    public const string key = "test_game";

    void Awake()
    {
        packetProcessor = new NetPacketProcessor();
        client = new NetManager(this);
        client.UnconnectedMessagesEnabled = true;
        client.UpdateTime = 15;
        client.Start();

        packetProcessor.RegisterNestedType<Vector3Serializable>();
        packetProcessor.RegisterNestedType<QuaternionSerializable>();
        packetProcessor.SubscribeReusable<PlayerInstantiatePacket, NetPeer>(OnInstantiatePlayer);
    }

    void Start()
    {
        
    }

    float updateTimer = 0;

    int id1 = 0;

    void Update()
    {
        updateTimer += Time.deltaTime;

        if(client != null)
        {
            client.PollEvents();

            var peer = client.FirstPeer;

            if(peer != null && peer.ConnectionState == ConnectionState.Connected)
            {
                if(updateTimer >= 0.015f)
                {
                    /*TestPacket packet1 = new TestPacket
                    {
                        id = id1,
                        text = "asd"
                    };

                    peer.Send(packetProcessor.Write(packet1), DeliveryMethod.ReliableOrdered);*/

                    id1++;

                    updateTimer = 0;
                }
            }
            else
            {
                client.Connect(ip, port, key);
            }
        }
    }

    void OnDestroy()
    {
        packetProcessor.RemoveSubscription<PlayerInstantiatePacket>();
    }

    void INetEventListener.OnPeerConnected(NetPeer peer)
    {
        Debug.Log("Success");
    }

    void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        //throw new System.NotImplementedException();
    }

    void INetEventListener.OnNetworkError(IPEndPoint endPoint, SocketError socketError)
    {
        //throw new System.NotImplementedException();
    }

    void INetEventListener.OnNetworkReceive(NetPeer peer, NetPacketReader reader, DeliveryMethod deliveryMethod)
    {
        packetProcessor.ReadAllPackets(reader, peer);
        //throw new System.NotImplementedException();
    }

    void INetEventListener.OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
    {
        //throw new System.NotImplementedException();
    }

    void INetEventListener.OnNetworkLatencyUpdate(NetPeer peer, int latency)
    {
        //throw new System.NotImplementedException();
    }

    void INetEventListener.OnConnectionRequest(ConnectionRequest request)
    {
        //throw new System.NotImplementedException();
    }

    void OnInstantiatePlayer(PlayerInstantiatePacket packet, NetPeer peer)
    {
        Debug.Log("Instantiate ID: " + packet.id);

        GameObject newObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        newObj.transform.position = TransformConverter.ToVector3(packet.position);
        newObj.transform.rotation = TransformConverter.ToQuaternion(packet.rotation);
    }
}

public class TestPacket
{
    public string text { get; set; }
    public int id { get; set; }
}
