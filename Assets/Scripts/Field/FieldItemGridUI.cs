using App;
using UnityEngine;
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

		public static void Clear(RectTransform root)
		{
			if (root == null)
				return;
			for (int index = root.childCount - 1; index >= 0; index--)
				Object.Destroy(root.GetChild(index).gameObject);
		}

		static void DestroyLayoutComponents(RectTransform root)
		{
			VerticalLayoutGroup vertical = root.GetComponent<VerticalLayoutGroup>();
			if (vertical != null)
				Object.Destroy(vertical);
			HorizontalLayoutGroup horizontal = root.GetComponent<HorizontalLayoutGroup>();
			if (horizontal != null)
				Object.Destroy(horizontal);
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
}
