using App;
using Protocol;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Scenes
{
	public sealed class BattleSceneFlow : MonoBehaviour
	{
		const string BattleSceneName = "BattleScene";

		static BattleSceneFlow _instance;
		bool _subscribed;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		static void Bootstrap()
		{
			if (_instance != null)
				return;

			GameObject go = new GameObject("@BattleSceneFlow");
			DontDestroyOnLoad(go);
			_instance = go.AddComponent<BattleSceneFlow>();
		}

		void OnEnable()
		{
			TrySubscribe();
		}

		void Update()
		{
			if (_subscribed == false)
				TrySubscribe();
		}

		void OnDisable()
		{
			if (_subscribed && GameRoot.Instance != null)
				GameRoot.Instance.Network.EnterBattleReceived -= OnEnterBattleReceived;

			_subscribed = false;
		}

		void TrySubscribe()
		{
			if (_subscribed || GameRoot.Instance == null || GameRoot.Instance.Network == null)
				return;

			GameRoot.Instance.Network.EnterBattleReceived += OnEnterBattleReceived;
			_subscribed = true;
		}

		void OnEnterBattleReceived(S_ENTER_BATTLE packet)
		{
			if (packet == null || packet.Success == false)
				return;

			if (SceneManager.GetActiveScene().name == BattleSceneName)
				return;

			Debug.Log($"Loading {BattleSceneName}. battleId={packet.BattleId}, mapId={packet.MapId}");
			SceneManager.LoadScene(BattleSceneName);
		}
	}
}
