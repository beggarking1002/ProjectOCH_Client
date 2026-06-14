using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace ServerCore
{
	public class Connector
	{
		Func<Session> _sessionFactory;

		public Action OnSuccessCallback { get; set; }
		public Action OnFailedCallback { get; set; }

		public void Connect(IPEndPoint endPoint, Func<Session> sessionFactory)
		{
			// 휴대폰 설정
			Socket socket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
			_sessionFactory = sessionFactory;

			SocketAsyncEventArgs args = new SocketAsyncEventArgs();
			args.Completed += OnConnectCompleted;
			args.RemoteEndPoint = endPoint;
			args.UserToken = socket;

			RegisterConnect(args);
		}

		void RegisterConnect(SocketAsyncEventArgs args)
		{
			Socket socket = args.UserToken as Socket;
			if (socket == null)
				return;

			try
			{
				bool pending = socket.ConnectAsync(args);
				if (pending == false)
					OnConnectCompleted(null, args);
			}
			catch (Exception ex)
			{
				Debug.Log($"ConnectAsync Failed {ex}");
			}
		}

		void OnConnectCompleted(object sender, SocketAsyncEventArgs args)
		{
			if (args.SocketError == SocketError.Success)
			{
				Session session = _sessionFactory.Invoke();
				session.Start(args.ConnectSocket);
				session.OnConnected(args.RemoteEndPoint);

				Debug.Log($"OnConnectCompleted");
				OnSuccessCallback?.Invoke();
			}
			else
			{
				Debug.Log($"OnConnectCompleted Fail: {args.SocketError}");
				OnFailedCallback?.Invoke();
			}
		}
	}
}
