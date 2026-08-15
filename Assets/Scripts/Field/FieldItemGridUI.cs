using App;
using UnityEngine;
using UnityEngine.UI;

namespace Field
{
	internal static class FieldItemGridUI
	{
		public static void Configure(RectTransform root, int columns = 4)
		{
			if (root == null)
				return;
			DestroyLayoutComponents(root);
			GridLayoutGroup layout = root.gameObject.GetComponent<GridLayoutGroup>();
			if (layout == null)
				layout = root.gameObject.AddComponent<GridLayoutGroup>();
			layout.padding = new RectOffset(16, 16, 16, 16);
			layout.cellSize = new Vector2(104, 112);
			layout.spacing = new Vector2(12, 12);
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
			frame.color = new Color(0.16f, 0.11f, 0.06f, 0.96f);
			Button button = slot.GetComponent<Button>();
			button.targetGraphic = frame;
			button.interactable = interactable;
			button.transition = Selectable.Transition.None;

			Image icon = CreateImage("Icon", slot.GetComponent<RectTransform>(), new Vector2(7, 22), new Vector2(-7, -7));
			icon.preserveAspect = true;
			icon.color = Color.white;
			CreateText("Price", slot.GetComponent<RectTransform>(), topLeftText, TextAnchor.UpperLeft, 13, new Vector2(6, 4), new Vector2(-6, -4));
			CreateText("Quantity", slot.GetComponent<RectTransform>(), $"x{quantity}", TextAnchor.LowerRight, 15, new Vector2(6, 4), new Vector2(-6, -4));
			CreateText("Name", slot.GetComponent<RectTransform>(), itemName, TextAnchor.LowerCenter, 13, new Vector2(6, 2), new Vector2(-6, 18));
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
