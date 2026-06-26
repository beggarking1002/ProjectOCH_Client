using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace ServerCore
{
	public abstract class PacketSession : Session
	{
		public const int HeaderSize = 4;

		// [size(2)][packetId(2)][payload]
		public sealed override int OnRecv(ArraySegment<byte> buffer)
		{
			int processLen = 0;

			while (true)
			{
				if (buffer.Array == null)
					return -1;

				if (buffer.Count < HeaderSize)
					break;

				ushort dataSize = BitConverter.ToUInt16(buffer.Array, buffer.Offset);
				if (dataSize < HeaderSize)
					return -1;

				if (buffer.Count < dataSize)
					break;

				OnRecvPacket(new ArraySegment<byte>(buffer.Array, buffer.Offset, dataSize));

				processLen += dataSize;
				buffer = new ArraySegment<byte>(buffer.Array, buffer.Offset + dataSize, buffer.Count - dataSize);
			}

			return processLen;
		}

		public abstract void OnRecvPacket(ArraySegment<byte> buffer);
	}

	public abstract class Session
	{
		Socket _socket;
		int _disconnected;

		readonly RecvBuffer _recvBuffer = new RecvBuffer(65535);

		readonly object _lock = new object();
		readonly Queue<ArraySegment<byte>> _sendQueue = new Queue<ArraySegment<byte>>();
		readonly List<ArraySegment<byte>> _pendingList = new List<ArraySegment<byte>>();
		readonly SocketAsyncEventArgs _sendArgs = new SocketAsyncEventArgs();
		readonly SocketAsyncEventArgs _recvArgs = new SocketAsyncEventArgs();

		public abstract void OnConnected(EndPoint endPoint);
		public abstract int OnRecv(ArraySegment<byte> buffer);
		public abstract void OnSend(int numOfBytes);
		public abstract void OnDisconnected(EndPoint endPoint);

		public void Start(Socket socket)
		{
			_socket = socket;

			_recvArgs.Completed += OnRecvCompleted;
			_sendArgs.Completed += OnSendCompleted;

			RegisterRecv();
		}

		public void Send(List<ArraySegment<byte>> sendBuffList)
		{
			if (sendBuffList.Count == 0)
				return;

			lock (_lock)
			{
				if (_disconnected == 1 || _socket == null)
					return;

				foreach (ArraySegment<byte> sendBuff in sendBuffList)
					_sendQueue.Enqueue(sendBuff);

				if (_pendingList.Count == 0)
					RegisterSend();
			}
		}

		public void Send(ArraySegment<byte> sendBuff)
		{
			lock (_lock)
			{
				if (_disconnected == 1 || _socket == null)
					return;

				_sendQueue.Enqueue(sendBuff);
				if (_pendingList.Count == 0)
					RegisterSend();
			}
		}

		public void Disconnect()
		{
			if (Interlocked.Exchange(ref _disconnected, 1) == 1)
				return;

			Socket socket = _socket;
			EndPoint endPoint = GetRemoteEndPoint(socket);

			try
			{
				OnDisconnected(endPoint);
			}
			catch (Exception e)
			{
				Debug.Log(e);
			}

			try
			{
				socket?.Shutdown(SocketShutdown.Both);
			}
			catch (Exception)
			{
				// The peer may already have closed the connection.
			}

			try
			{
				socket?.Close();
			}
			catch (Exception e)
			{
				Debug.Log(e);
			}
			finally
			{
				Clear();
			}
		}

		public bool IsConnected()
		{
			return _disconnected == 0 && _socket != null && _socket.Connected;
		}

		static EndPoint GetRemoteEndPoint(Socket socket)
		{
			try
			{
				return socket?.RemoteEndPoint;
			}
			catch (Exception)
			{
				return null;
			}
		}

		void Clear()
		{
			lock (_lock)
			{
				_sendQueue.Clear();
				_pendingList.Clear();
				_socket = null;
			}
		}

		void RegisterSend()
		{
			if (_disconnected == 1 || _socket == null)
				return;

			while (_sendQueue.Count > 0)
			{
				ArraySegment<byte> buff = _sendQueue.Dequeue();
				_pendingList.Add(buff);
			}

			_sendArgs.BufferList = _pendingList;

			try
			{
				bool pending = _socket.SendAsync(_sendArgs);
				if (pending == false)
					OnSendCompleted(null, _sendArgs);
			}
			catch (Exception e)
			{
				Debug.Log($"RegisterSend Failed {e}");
				Disconnect();
			}
		}

		void OnSendCompleted(object sender, SocketAsyncEventArgs args)
		{
			lock (_lock)
			{
				if (args.BytesTransferred > 0 && args.SocketError == SocketError.Success)
				{
					try
					{
						_sendArgs.BufferList = null;
						_pendingList.Clear();

						OnSend(_sendArgs.BytesTransferred);

						if (_sendQueue.Count > 0)
							RegisterSend();
					}
					catch (Exception e)
					{
						Debug.Log($"OnSendCompleted Failed {e}");
						Disconnect();
					}
				}
				else
				{
					Disconnect();
				}
			}
		}

		void RegisterRecv()
		{
			if (_disconnected == 1 || _socket == null)
				return;

			_recvBuffer.Clean();
			ArraySegment<byte> segment = _recvBuffer.WriteSegment;
			_recvArgs.SetBuffer(segment.Array, segment.Offset, segment.Count);

			try
			{
				bool pending = _socket.ReceiveAsync(_recvArgs);
				if (pending == false)
					OnRecvCompleted(null, _recvArgs);
			}
			catch (Exception e)
			{
				Debug.Log($"RegisterRecv Failed {e}");
				Disconnect();
			}
		}

		void OnRecvCompleted(object sender, SocketAsyncEventArgs args)
		{
			if (args.BytesTransferred <= 0 || args.SocketError != SocketError.Success)
			{
				Disconnect();
				return;
			}

			try
			{
				if (_recvBuffer.OnWrite(args.BytesTransferred) == false)
				{
					Disconnect();
					return;
				}

				int processLen = OnRecv(_recvBuffer.ReadSegment);
				if (processLen < 0 || _recvBuffer.DataSize < processLen)
				{
					Disconnect();
					return;
				}

				if (_recvBuffer.OnRead(processLen) == false)
				{
					Disconnect();
					return;
				}

				RegisterRecv();
			}
			catch (Exception e)
			{
				Debug.Log($"OnRecvCompleted Failed {e}");
				Disconnect();
			}
		}
	}
}
