using App;
using Protocol;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Scenes
{
	public sealed class BattleSceneFlow : MonoBehaviour
	{
		const string BattleSceneName = "BattleScene";
		const string FieldSceneName = "FieldScene";

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
			{
				GameRoot.Instance.Network.EnterBattleReceived -= OnEnterBattleReceived;
				GameRoot.Instance.Network.BattleResultAckReceived -= OnBattleResultAckReceived;
			}

			_subscribed = false;
		}

		void TrySubscribe()
		{
			if (_subscribed || GameRoot.Instance == null || GameRoot.Instance.Network == null)
				return;

			GameRoot.Instance.Network.EnterBattleReceived += OnEnterBattleReceived;
			GameRoot.Instance.Network.BattleResultAckReceived += OnBattleResultAckReceived;
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

		void OnBattleResultAckReceived(S_BATTLE_RESULT_ACK packet)
		{
			if (packet == null)
				return;

			if (packet.Success == false)
			{
				Debug.LogWarning($"Battle result ack rejected. battleId={packet.BattleId}, reason={packet.Reason}");
				return;
			}

			if (SceneManager.GetActiveScene().name == FieldSceneName)
				return;

			Debug.Log($"Loading {FieldSceneName} after battle result ack. battleId={packet.BattleId}");
			SceneManager.LoadScene(FieldSceneName);
		}
	}
}
