using App;
using UnityEngine;
using UnityEngine.UI;

namespace Field
{
	[DisallowMultipleComponent]
	public sealed class FieldSceneHudController : MonoBehaviour
	{
		[SerializeField] Text statusText;
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
			if (statusText == null)
				return;
			if (state == null)
			{
				statusText.text = "원정 상태\n포만도  - / -\n갈증  - / -";
				return;
			}

			statusText.text = $"원정 상태\n포만도  {state.Satiety} / {state.MaxSatiety}\n갈증  {state.Thirst} / {state.MaxThirst}";
		}

		void OpenInventory()
		{
			FindFirstObjectByType<FieldObjectManager>()?.TogglePlayerInventoryUi();
		}
	}
}
