using UnityEngine;
using System.Collections;
using DarkRift;

public class ClientScript : MonoBehaviour
{
	public int dataSize = 3 * 1024 * 1024;

	DarkRift.Client.Unity.UnityClient _client;
	System.Diagnostics.Stopwatch _sw = new System.Diagnostics.Stopwatch();

	void Start()
	{
		_client = GetComponent<DarkRift.Client.Unity.UnityClient>();
		_client.MessageReceived += __OnMessageReceived__;
	}

	void __OnMessageReceived__(object sender, DarkRift.Client.MessageReceivedEventArgs e)
	{
		Debug.Log("fff");
		using (var message = e.GetMessage())
		{
			using (var reader = message.GetReader())
			{
				var tag = message.Tag;
				switch (tag)
				{
					case 0:
						_SendBegin();
						_Send();
						_SendBegin();
						_Send();
						_SendBegin();
						_Send();
						_SendBegin();
						_Send();
						_SendBegin();
						_Send();
						_SendBegin();
						_Send();
						break;
				}
			}
		}
	}

	void _SendBegin()
	{
		using (DarkRiftWriter messageDataWriter = DarkRiftWriter.Create())
		{
			messageDataWriter.Write(dataSize);
			using (Message myMessage = Message.Create(0, messageDataWriter))
			{
				_client.SendMessage(myMessage, SendMode.Reliable);
			}
		}

		Debug.Log("asdf");
	}

	void _Send()
	{
		using (DarkRiftWriter messageDataWriter = DarkRiftWriter.Create())
		{
			messageDataWriter.Write(new byte[dataSize]);
			using (Message myMessage = Message.Create(1, messageDataWriter))
			{
				_client.SendMessage(myMessage, SendMode.Reliable);
			}
		}

		Debug.Log("asdf2");
	}
}
