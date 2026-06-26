using System;
using Networking;

namespace App
{
	public sealed class AppServices : IDisposable
	{
		public NetworkService Network { get; } = new NetworkService();

		public void Initialize(string gameServerHost, int gameServerPort, bool verifyWithLoginPacket)
		{
			Network.Initialize(gameServerHost, gameServerPort, verifyWithLoginPacket);
		}

		public void Tick()
		{
			Network.Tick();
		}

		public void Dispose()
		{
			Network.Dispose();
		}
	}
}
