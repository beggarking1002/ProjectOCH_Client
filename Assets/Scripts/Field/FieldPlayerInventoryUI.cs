using System.Threading.Tasks;
using App;
using UnityEngine;
using UnityEngine.UI;

namespace Field
{
	[DisallowMultipleComponent]
	public sealed class FieldPlayerInventoryUI : MonoBehaviour
	{
		FieldEconomyRepository _repository;
		RectTransform _grid;
		Text _title;
		Text _capacityText;
		bool _subscribed;

		async void OnEnable()
		{
			BindView();
			Subscribe();
			_repository = await FieldEconomyRepository.LoadAsync();
			if (this == null || isActiveAndEnabled == false)
				return;
			await _repository.PreloadAllItemIconsAsync();
			if (this == null || isActiveAndEnabled == false)
				return;
			Render(GameRoot.Instance?.Network.LastExpeditionState);
		}

		void OnDisable()
		{
			Unsubscribe();
		}

		void OnDestroy()
		{
			Unsubscribe();
		}

		void BindView()
		{
			_grid = transform.Find("Window/BagGrid") as RectTransform;
			_title = transform.Find("Window/TitleFrame/TitleText")?.GetComponent<Text>();
			_capacityText = transform.Find("Window/CapacityText")?.GetComponent<Text>();
			FieldItemGridUI.Configure(_grid, FieldItemGridUI.ArtworkColumnCount);
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
			if (_repository == null || _grid == null)
				return;
			if (_title != null)
			{
				int gold = state?.Gold ?? 0;
				_title.text = $"인벤토리  ·  보유금 {gold}G";
			}
			if (_capacityText != null)
			{
				int occupiedSlots = state?.Inventory.Count ?? 0;
				int maxSlots = FieldItemGridUI.ArtworkColumnCount * FieldItemGridUI.ArtworkColumnCount;
				_capacityText.text = $"{occupiedSlots} / {maxSlots}";
			}

			FieldItemGridUI.Clear(_grid);
			if (state == null)
				return;
			foreach (Protocol.ExpeditionItemStackInfo stack in state.Inventory)
			{
				string expiry = stack.RemainingShelfLifeSeconds < 0
					? "무제한"
					: $"{Mathf.CeilToInt(stack.RemainingShelfLifeSeconds / 60f)}분";
				Button slot = FieldItemGridUI.CreateSlot(_grid, ItemName(stack.ItemId), stack.Quantity, expiry, true);
				FieldItemGridUI.SetIconAsync(slot, _repository, stack.ItemId);
			}
		}

		string ItemName(string itemId)
		{
			return _repository.TryGetItem(itemId, out FieldItemDefinition item) && string.IsNullOrWhiteSpace(item.Name) == false
				? item.Name
				: itemId;
		}
	}
}
