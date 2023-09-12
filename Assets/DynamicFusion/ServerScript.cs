using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DarkRift;
using System;
using System.Diagnostics;

public class ServerScript : MonoBehaviour
{
	DarkRift.Server.Unity.XmlUnityServer _server;
	DarkRift.Server.IClient _client;
    public float Time_Sending = 0;
    public bool isConnected = false;


    Stopwatch sw_sending = new Stopwatch();

    void Start()
	{
		_server = GetComponent<DarkRift.Server.Unity.XmlUnityServer>();
		_server.Server.ClientManager.ClientConnected += __OnClientConnected__;
	}

    void __OnClientConnected__(object sender, DarkRift.Server.ClientConnectedEventArgs args)
    {
        _client = args.Client;
        _client.MessageReceived += __OnMessageReceived__;
        isConnected = true;
    }

    // type 0 = vertex + texture, type 1 = teuxtre, type 2 = vertex
    public void sendData(byte[] data, int size, ushort type = 0)
    {
        sw_sending.Start();

        using (DarkRiftWriter messageDataWriter = DarkRiftWriter.Create())
        {
            byte[] sendData = new byte[size];
            Buffer.BlockCopy(data, 0, sendData, 0, size);

            data = sendData;
            messageDataWriter.Write(data);

            //Build the message data
            using (Message myMessage = Message.Create(type, messageDataWriter))
            {
                //Send a message to a client
                _client.SendMessage(myMessage, SendMode.Reliable);
            }
        }

        sw_sending.Stop();
        Time_Sending = (float)sw_sending.ElapsedMilliseconds;
        sw_sending.Reset();
    }

	void __OnMessageReceived__(object sender, DarkRift.Server.MessageReceivedEventArgs e)
	{
		
	}
}
