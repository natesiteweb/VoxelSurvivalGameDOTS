using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using LiteNetLib;
using LiteNetLib.Utils;

public class NetworkServer : MonoBehaviour, INetEventListener
{
    NetManager server;
    NetPacketProcessor packetProcessor;

    public const int maxPlayers = 64;
    public const int port = 904;
    public const string key = "test_game";

    public int currentPlayerID = 0;

    public Dictionary<int, Vector3> playerPositionList = new Dictionary<int, Vector3>();
    public Dictionary<int, Quaternion> playerRotationList = new Dictionary<int, Quaternion>();
    public Dictionary<EndPoint, int> playerIdList = new Dictionary<EndPoint, int>();

    void Awake()
    {
        packetProcessor = new NetPacketProcessor();

        server = new NetManager(this)
        {
            AutoRecycle = true
        };

        server.Start(port);
        server.BroadcastReceiveEnabled = true;

        packetProcessor.RegisterNestedType<Vector3Serializable>();
        packetProcessor.RegisterNestedType<QuaternionSerializable>();

        //packetProcessor.SubscribeReusable<TestPacket, NetPeer>(OnJoinReceived);
    }

    void OnJoinReceived(TestPacket packet, NetPeer peer)
    {
        Debug.Log(packet.text + ":" + packet.id);
    }

    float updateTimer = 0;

    void Update()
    {
        updateTimer += Time.deltaTime;

        if(updateTimer >= 0.015f)
        {
            server.PollEvents();
            updateTimer = 0;
        }
    }

    void OnDestroy()
    {
        //packetProcessor.RemoveSubscription<>
        server.Stop();
    }

    void INetEventListener.OnPeerConnected(NetPeer peer)
    {
        Debug.Log("Joined from: " + peer.EndPoint.ToString());
        Debug.Log("Instantiating: " + peer.EndPoint.ToString() + "with id: " + currentPlayerID);

        playerIdList.Add(peer.EndPoint, currentPlayerID);
        playerPositionList.Add(currentPlayerID, Vector3.zero);
        playerRotationList.Add(currentPlayerID, Quaternion.identity);

        PlayerInstantiatePacket packet = new PlayerInstantiatePacket
        {
            id = currentPlayerID,
            position = TransformConverter.ToVector3Serializable(new Vector3(0.1f, 1f, -1f)),
            rotation = TransformConverter.ToQuaternionSerializable(Quaternion.identity)
        };

        server.SendToAll(packetProcessor.Write(packet), DeliveryMethod.ReliableOrdered);

        currentPlayerID++;
    }

    void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        //throw new System.NotImplementedException();
    }

    void INetEventListener.OnNetworkError(IPEndPoint endPoint, SocketError socketError)
    {
        Debug.Log("[S] NetworkError: " + socketError);
    }

    void INetEventListener.OnNetworkReceive(NetPeer peer, NetPacketReader reader, DeliveryMethod deliveryMethod)
    {
        packetProcessor.ReadAllPackets(reader, peer);
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
        if(server.ConnectedPeersCount < maxPlayers)
        {
            request.AcceptIfKey(key);
        }
        else
        {
            request.Reject();
        }
    }
}

public class TestPacket
{
    public string text { get; set; }
    public int id { get; set; }
}