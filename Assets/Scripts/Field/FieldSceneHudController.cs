using App;
using UnityEngine;
using UnityEngine.UI;

namespace Field
{
	[DisallowMultipleComponent]
	public sealed class FieldSceneHudController : MonoBehaviour
	{
		[SerializeField] Text statusText;
		[SerializeField] Text resourceText;
		[SerializeField] Button inventoryButton;
		bool _subscribed;

		void Awake()
		{
			inventoryButton?.onClick.AddListener(OpenInventory);
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
				// Supply and village reputation have no authoritative packet values yet.
				// Keep their fixed HUD positions ready for those fields.
				resourceText.text = $"금화  {gold}                         보급품  0                         마을 평판  0";
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
	}
}
