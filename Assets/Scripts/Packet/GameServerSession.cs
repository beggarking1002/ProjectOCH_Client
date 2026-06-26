using System;
using System.Net;
using ServerCore;
using UnityEngine;

namespace Networking
{
	public sealed class GameServerSession : PacketSession
	{
		public event Action<EndPoint> Connected;
		public event Action<EndPoint> Disconnected;
		public event Action<int> Sent;

		public override void OnConnected(EndPoint endPoint)
		{
			Debug.Log($"Connected to game server: {endPoint}");
			Connected?.Invoke(endPoint);
		}

		public override void OnRecvPacket(ArraySegment<byte> buffer)
		{
			ClientPacketHandler.Instance.HandlePacket(this, buffer);
		}

		public override void OnSend(int numOfBytes)
		{
			Sent?.Invoke(numOfBytes);
		}

		public override void OnDisconnected(EndPoint endPoint)
		{
			Debug.Log($"Disconnected from game server: {endPoint}");
			Disconnected?.Invoke(endPoint);
		}
	}
}
