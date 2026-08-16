using App;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;

namespace Field
{
	[DisallowMultipleComponent]
	public sealed class FieldSceneHudController : MonoBehaviour
	{
		const string FieldVillageQuestUiAddress = "FieldVillageQuestUI";

		[SerializeField] Text statusText;
		[SerializeField] Text resourceText;
		[SerializeField] Button inventoryButton;
		[SerializeField] Button questTrackerButton;
		bool _subscribed;
		bool _isQuestTrackerOpening;

		void Awake()
		{
			inventoryButton?.onClick.AddListener(OpenInventory);
			questTrackerButton?.onClick.AddListener(OpenQuestTracker);
		}

		void OnEnable()
		{
			Subscribe();
			Render(GameRoot.Instance?.Network.LastExpeditionState);
		}

		void OnDisable()
		{
			Unsubscribe();
		}

		void OnDestroy()
		{
			inventoryButton?.onClick.RemoveListener(OpenInventory);
			questTrackerButton?.onClick.RemoveListener(OpenQuestTracker);
			Unsubscribe();
		}

		void Subscribe()
		{
			if (_subscribed || GameRoot.Instance == null)
				return;
			GameRoot.Instance.Network.ExpeditionStateReceived += Render;
			_subscribed = true;
		}

		void Unsubscribe()
		{
			if (_subscribed == false || GameRoot.Instance == null)
				return;
			GameRoot.Instance.Network.ExpeditionStateReceived -= Render;
			_subscribed = false;
		}

		void Render(Protocol.S_EXPEDITION_STATE state)
		{
			if (resourceText != null)
			{
				int gold = state?.Gold ?? 0;
				int fame = state?.Fame ?? 0;
				resourceText.text = $"금화  {gold}                         보급품  0                         명성  {fame}";
			}

			if (statusText == null)
				return;

			Protocol.S_LOGIN login = GameRoot.Instance?.Network.LastLogin;
			Protocol.S_ENTER_GAME enterGame = GameRoot.Instance?.Network.LastEnterGame;
			string identityText;
			if (login != null && login.AccountId != 0)
			{
				string name = string.IsNullOrWhiteSpace(login.DisplayName) ? "Google 계정" : login.DisplayName;
				identityText = $"{name}  ·  ID {login.AccountId}";
			}
			else
			{
				ulong temporaryId = enterGame?.Player?.ObjectId ?? 0;
				identityText = temporaryId == 0 ? "개발용 계정  ·  ID -" : $"개발용 계정  ·  ID {temporaryId}";
			}

			if (state == null)
			{
				statusText.text = $"{identityText}\n포만도  - / -\n갈증  - / -";
				return;
			}

			statusText.text = $"{identityText}\n포만도  {state.Satiety} / {state.MaxSatiety}\n갈증  {state.Thirst} / {state.MaxThirst}";
		}

		void OpenInventory()
		{
			FindFirstObjectByType<FieldObjectManager>()?.TogglePlayerInventoryUi();
		}

		async void OpenQuestTracker()
		{
			if (_isQuestTrackerOpening)
				return;

			_isQuestTrackerOpening = true;
			AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync(FieldVillageQuestUiAddress);
			await handle.Task;
			_isQuestTrackerOpening = false;
			if (this == null || handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
			{
				if (handle.IsValid())
					Addressables.Release(handle);
				Debug.LogError($"Failed to open quest tracker UI: {FieldVillageQuestUiAddress}");
				return;
			}

			FieldVillageQuestUI questUi = handle.Result.GetComponent<FieldVillageQuestUI>();
			if (questUi == null)
			{
				Addressables.ReleaseInstance(handle);
				Debug.LogError("Quest tracker prefab is missing FieldVillageQuestUI.");
				return;
			}
			questUi.ShowTracker();
		}
	}
}
