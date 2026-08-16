using System;
using System.Collections.Generic;
using System.Text;
using App;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Field
{
	internal static class FieldItemGridUI
	{
		// BagGrid.png is authored as a 7 x 7 board. Keep runtime slots aligned
		// to the grid painted into the prefab instead of drawing a second grid.
		public const int ArtworkColumnCount = 7;

		public static void Configure(RectTransform root, int columns = ArtworkColumnCount)
		{
			if (root == null)
				return;
			DestroyLayoutComponents(root);
			GridLayoutGroup layout = root.gameObject.GetComponent<GridLayoutGroup>();
			if (layout == null)
				layout = root.gameObject.AddComponent<GridLayoutGroup>();
			columns = Mathf.Max(1, columns);
			layout.padding = new RectOffset(0, 0, 0, 0);
			layout.cellSize = new Vector2(root.rect.width / columns, root.rect.height / ArtworkColumnCount);
			layout.spacing = Vector2.zero;
			layout.childAlignment = TextAnchor.UpperLeft;
			layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
			layout.constraintCount = columns;
		}

		public static Button CreateSlot(RectTransform parent, string itemName, int quantity, string topLeftText, bool interactable)
		{
			GameObject slot = new GameObject($"ItemSlot_{itemName}", typeof(RectTransform), typeof(Image), typeof(Button));
			slot.layer = parent.gameObject.layer;
			slot.transform.SetParent(parent, false);
			Image frame = slot.GetComponent<Image>();
			// BagGrid already contains the slot artwork. This is only a click target.
			frame.color = Color.clear;
			Button button = slot.GetComponent<Button>();
			button.targetGraphic = frame;
			button.interactable = interactable;
			button.transition = Selectable.Transition.None;

			Image icon = CreateImage("Icon", slot.GetComponent<RectTransform>(), new Vector2(7, 7), new Vector2(-7, -7));
			icon.preserveAspect = true;
			icon.color = Color.white;
			CreateText("Price", slot.GetComponent<RectTransform>(), topLeftText, TextAnchor.UpperLeft, 11, new Vector2(4, 3), new Vector2(-4, -3));
			CreateText("Quantity", slot.GetComponent<RectTransform>(), $"x{quantity}", TextAnchor.LowerRight, 12, new Vector2(4, 3), new Vector2(-4, -3));
			return button;
		}

		public static async void SetIconAsync(Button slot, FieldEconomyRepository repository, string itemId)
		{
			if (slot == null || repository == null)
				return;
			Sprite sprite = await repository.LoadItemIconAsync(itemId);
			if (slot == null || sprite == null)
				return;
			Image icon = slot.transform.Find("Icon")?.GetComponent<Image>();
			if (icon != null)
				icon.sprite = sprite;
		}

		public static void BindTooltip(Button slot, string content)
		{
			if (slot == null || string.IsNullOrWhiteSpace(content))
				return;
			FieldItemTooltipTrigger trigger = slot.gameObject.GetComponent<FieldItemTooltipTrigger>();
			if (trigger == null)
				trigger = slot.gameObject.AddComponent<FieldItemTooltipTrigger>();
			trigger.SetContent(content);
		}

		public static void Clear(RectTransform root)
		{
			if (root == null)
				return;
			for (int index = root.childCount - 1; index >= 0; index--)
				UnityEngine.Object.Destroy(root.GetChild(index).gameObject);
		}

		static void DestroyLayoutComponents(RectTransform root)
		{
			VerticalLayoutGroup vertical = root.GetComponent<VerticalLayoutGroup>();
			if (vertical != null)
				UnityEngine.Object.Destroy(vertical);
			HorizontalLayoutGroup horizontal = root.GetComponent<HorizontalLayoutGroup>();
			if (horizontal != null)
				UnityEngine.Object.Destroy(horizontal);
		}

		static Image CreateImage(string name, RectTransform parent, Vector2 offsetMin, Vector2 offsetMax)
		{
			GameObject child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
			child.layer = parent.gameObject.layer;
			child.transform.SetParent(parent, false);
			RectTransform rect = child.GetComponent<RectTransform>();
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			rect.offsetMin = offsetMin;
			rect.offsetMax = offsetMax;
			child.GetComponent<Image>().raycastTarget = false;
			return child.GetComponent<Image>();
		}

		static Text CreateText(string name, RectTransform parent, string value, TextAnchor alignment, int fontSize, Vector2 offsetMin, Vector2 offsetMax)
		{
			GameObject child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
			child.layer = parent.gameObject.layer;
			child.transform.SetParent(parent, false);
			RectTransform rect = child.GetComponent<RectTransform>();
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			rect.offsetMin = offsetMin;
			rect.offsetMax = offsetMax;
			Text text = child.GetComponent<Text>();
			text.text = value;
			text.font = GameRoot.UiFont != null ? GameRoot.UiFont : Resources.GetBuiltinResource<Font>("Arial.ttf");
			text.fontSize = fontSize;
			text.alignment = alignment;
			text.color = new Color(0.96f, 0.84f, 0.52f, 1f);
			text.raycastTarget = false;
			text.horizontalOverflow = HorizontalWrapMode.Wrap;
			text.verticalOverflow = VerticalWrapMode.Truncate;
			return text;
		}
	}

	internal sealed class FieldInventoryItemGroup
	{
		public string ItemId;
		public int TotalQuantity;
		public readonly List<Protocol.ExpeditionItemStackInfo> Batches = new List<Protocol.ExpeditionItemStackInfo>();
	}

	internal static class FieldInventoryGrouping
	{
		public static List<FieldInventoryItemGroup> Build(IEnumerable<Protocol.ExpeditionItemStackInfo> stacks)
		{
			List<FieldInventoryItemGroup> result = new List<FieldInventoryItemGroup>();
			Dictionary<string, FieldInventoryItemGroup> byItemId =
				new Dictionary<string, FieldInventoryItemGroup>(StringComparer.OrdinalIgnoreCase);
			if (stacks == null)
				return result;

			foreach (Protocol.ExpeditionItemStackInfo stack in stacks)
			{
				if (stack == null || string.IsNullOrWhiteSpace(stack.ItemId) || stack.Quantity <= 0)
					continue;
				if (byItemId.TryGetValue(stack.ItemId, out FieldInventoryItemGroup group) == false)
				{
					group = new FieldInventoryItemGroup { ItemId = stack.ItemId };
					byItemId.Add(stack.ItemId, group);
					result.Add(group);
				}
				group.TotalQuantity += stack.Quantity;
				group.Batches.Add(stack);
			}
			return result;
		}

		public static string EarliestExpiryLabel(FieldInventoryItemGroup group)
		{
			long earliestSeconds = long.MaxValue;
			foreach (Protocol.ExpeditionItemStackInfo batch in group.Batches)
			{
				if (batch.RemainingShelfLifeSeconds >= 0)
					earliestSeconds = Math.Min(earliestSeconds, batch.RemainingShelfLifeSeconds);
			}
			return earliestSeconds == long.MaxValue ? "무제한" : FormatRemainingMinutes(ToMinuteBucket(earliestSeconds));
		}

		public static string BuildExpiryTooltip(FieldInventoryItemGroup group, string itemName)
		{
			SortedDictionary<long, int> expiringBuckets = new SortedDictionary<long, int>();
			int unlimitedQuantity = 0;
			foreach (Protocol.ExpeditionItemStackInfo batch in group.Batches)
			{
				if (batch.RemainingShelfLifeSeconds < 0)
				{
					unlimitedQuantity += batch.Quantity;
					continue;
				}
				long minuteBucket = ToMinuteBucket(batch.RemainingShelfLifeSeconds);
				expiringBuckets[minuteBucket] = expiringBuckets.TryGetValue(minuteBucket, out int quantity)
					? quantity + batch.Quantity
					: batch.Quantity;
			}

			StringBuilder text = new StringBuilder();
			text.Append(itemName).Append("  x").Append(group.TotalQuantity).AppendLine();
			text.Append("유통기한별 수량");
			foreach (KeyValuePair<long, int> bucket in expiringBuckets)
				text.AppendLine().Append("· ").Append(FormatRemainingMinutes(bucket.Key)).Append(" : ").Append(bucket.Value).Append("개");
			if (unlimitedQuantity > 0)
				text.AppendLine().Append("· 무제한 : ").Append(unlimitedQuantity).Append("개");
			return text.ToString();
		}

		static long ToMinuteBucket(long remainingSeconds)
		{
			return Math.Max(1, (remainingSeconds + 59) / 60);
		}

		static string FormatRemainingMinutes(long minutes)
		{
			if (minutes < 60)
				return $"{minutes}분 이내";
			long hours = minutes / 60;
			long remainderMinutes = minutes % 60;
			return remainderMinutes == 0 ? $"{hours}시간 이내" : $"{hours}시간 {remainderMinutes}분 이내";
		}
	}

	internal sealed class FieldItemTooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
	{
		string _content;
		RectTransform _popup;
		Canvas _canvas;

		public void SetContent(string content)
		{
			_content = content;
		}

		public void OnPointerEnter(PointerEventData eventData)
		{
			Show(eventData);
		}

		public void OnPointerMove(PointerEventData eventData)
		{
			Position(eventData);
		}

		public void OnPointerExit(PointerEventData eventData)
		{
			Hide();
		}

		void OnDisable()
		{
			Hide();
		}

		void OnDestroy()
		{
			Hide();
		}

		void Show(PointerEventData eventData)
		{
			Hide();
			if (string.IsNullOrWhiteSpace(_content))
				return;
			_canvas = GetComponentInParent<Canvas>();
			if (_canvas == null)
				return;

			GameObject popup = new GameObject("ItemExpiryTooltip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
			popup.layer = gameObject.layer;
			popup.transform.SetParent(_canvas.transform, false);
			_popup = popup.GetComponent<RectTransform>();
			_popup.pivot = new Vector2(0f, 1f);
			int lineCount = _content.Split('\n').Length;
			_popup.sizeDelta = new Vector2(330f, 24f + lineCount * 22f);
			Image background = popup.GetComponent<Image>();
			background.color = new Color(0.08f, 0.055f, 0.03f, 0.96f);
			background.raycastTarget = false;

			GameObject labelObject = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
			labelObject.layer = gameObject.layer;
			labelObject.transform.SetParent(_popup, false);
			RectTransform labelRect = labelObject.GetComponent<RectTransform>();
			labelRect.anchorMin = Vector2.zero;
			labelRect.anchorMax = Vector2.one;
			labelRect.offsetMin = new Vector2(12f, 10f);
			labelRect.offsetMax = new Vector2(-12f, -10f);
			Text label = labelObject.GetComponent<Text>();
			label.font = GameRoot.UiFont != null ? GameRoot.UiFont : Resources.GetBuiltinResource<Font>("Arial.ttf");
			label.fontSize = 16;
			label.alignment = TextAnchor.UpperLeft;
			label.color = new Color(0.96f, 0.86f, 0.65f, 1f);
			label.raycastTarget = false;
			label.horizontalOverflow = HorizontalWrapMode.Wrap;
			label.verticalOverflow = VerticalWrapMode.Overflow;
			label.text = _content;
			_popup.SetAsLastSibling();
			Position(eventData);
		}

		void Position(PointerEventData eventData)
		{
			if (_popup == null || eventData == null || _canvas == null)
				return;
			Vector2 position = eventData.position + new Vector2(18f, -18f);
			float scale = Math.Max(0.01f, _canvas.scaleFactor);
			float width = _popup.sizeDelta.x * scale;
			float height = _popup.sizeDelta.y * scale;
			if (position.x + width > Screen.width)
				position.x = eventData.position.x - width - 18f;
			if (position.y - height < 0f)
				position.y = eventData.position.y + height + 18f;
			_popup.position = position;
		}

		void Hide()
		{
			if (_popup != null)
				Destroy(_popup.gameObject);
			_popup = null;
			_canvas = null;
		}
	}
}
