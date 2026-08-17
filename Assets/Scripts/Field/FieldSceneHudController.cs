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
		Image _playerEmblem;
		Button _playerEmblemButton;
		bool _subscribed;
		bool _isQuestTrackerOpening;

		void Awake()
		{
			BindPlayerEmblemButton();
			inventoryButton?.onClick.AddListener(OpenInventory);
			questTrackerButton?.onClick.AddListener(OpenQuestTracker);
		}

		void OnEnable()
		{
			Subscribe();
			Render(GameRoot.Instance?.Network.LastExpeditionState);
			RefreshPlayerEmblem();
		}

		void OnDisable()
		{
			Unsubscribe();
		}

		void OnDestroy()
		{
			inventoryButton?.onClick.RemoveListener(OpenInventory);
			questTrackerButton?.onClick.RemoveListener(OpenQuestTracker);
			_playerEmblemButton?.onClick.RemoveListener(OpenFieldPawnSelection);
			Unsubscribe();
		}

		void Subscribe()
		{
			if (_subscribed || GameRoot.Instance == null)
				return;
			GameRoot.Instance.Network.ExpeditionStateReceived += Render;
			GameRoot.Instance.Network.FieldPawnSelectionReceived += OnFieldPawnSelection;
			FieldBattleClassSelectionUI.VisualCatalogReady += RefreshPlayerEmblem;
			_subscribed = true;
		}

		void Unsubscribe()
		{
			FieldBattleClassSelectionUI.VisualCatalogReady -= RefreshPlayerEmblem;
			if (_subscribed == false || GameRoot.Instance == null)
				return;
			GameRoot.Instance.Network.ExpeditionStateReceived -= Render;
			GameRoot.Instance.Network.FieldPawnSelectionReceived -= OnFieldPawnSelection;
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

		void BindPlayerEmblemButton()
		{
			Transform emblemTransform = FindDeepChild(transform, "PlayerEmblem");
			if (emblemTransform == null)
				return;
			_playerEmblem = emblemTransform.GetComponent<Image>();
			if (_playerEmblem == null)
				return;
			_playerEmblem.raycastTarget = true;
			_playerEmblemButton = emblemTransform.GetComponent<Button>();
			if (_playerEmblemButton == null)
				_playerEmblemButton = emblemTransform.gameObject.AddComponent<Button>();
			_playerEmblemButton.targetGraphic = _playerEmblem;
			_playerEmblemButton.onClick.RemoveListener(OpenFieldPawnSelection);
			_playerEmblemButton.onClick.AddListener(OpenFieldPawnSelection);
		}

		void OpenFieldPawnSelection()
		{
			if (FieldBattleClassSelectionUI.OpenFieldPawnSelection() == false)
				Debug.LogWarning("Field pawn selection UI is not ready yet.");
		}

		void OnFieldPawnSelection(Protocol.S_FIELD_PAWN_SELECT packet)
		{
			ulong myObjectId = GameRoot.Instance?.Network.LastEnterGame?.Player?.ObjectId ?? 0;
			if (packet != null && packet.Success && packet.ObjectId == myObjectId)
				ApplyPlayerEmblem(packet.PawnClass);
		}

		void RefreshPlayerEmblem()
		{
			Protocol.PawnClass pawnClass = GameRoot.Instance?.Network.LastEnterGame?.Player?.FieldPawnClass ?? Protocol.PawnClass.BeigeIce;
			ApplyPlayerEmblem(pawnClass);
		}

		void ApplyPlayerEmblem(Protocol.PawnClass pawnClass)
		{
			if (_playerEmblem != null && FieldBattleClassSelectionUI.TryGetPawnClassIcon(pawnClass, out Sprite sprite))
				_playerEmblem.sprite = sprite;
		}

		static Transform FindDeepChild(Transform root, string targetName)
		{
			if (root == null)
				return null;
			if (root.name == targetName)
				return root;
			for (int index = 0; index < root.childCount; index++)
			{
				Transform found = FindDeepChild(root.GetChild(index), targetName);
				if (found != null)
					return found;
			}
			return null;
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
