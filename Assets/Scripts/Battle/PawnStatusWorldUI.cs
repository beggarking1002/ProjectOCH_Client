using UnityEngine;

namespace Battle
{
	[DisallowMultipleComponent]
	public sealed class PawnStatusWorldUI : MonoBehaviour
	{
		const int BackgroundSortingOrder = 38;
		const int FillSortingOrder = 39;
		const int TextSortingOrder = 40;
		const float BarWidth = 0.9f;
		const float BarHeight = 0.12f;
		const float TrackPadding = 0.018f;
		const float BarGap = 0.03f;

		static Sprite _barSprite;

		[SerializeField] SpriteRenderer _hpTrack;
		[SerializeField] SpriteRenderer _hpFill;
		[SerializeField] SpriteRenderer _armorTrack;
		[SerializeField] SpriteRenderer _armorFill;
		[SerializeField] TextMesh _hpText;
		[SerializeField] TextMesh _armorText;

		public void Initialize(bool isMine)
		{
			_ = isMine;
			EnsureReferences();
		}

		public void SetValues(int hp, int maxHp, int armor, int maxArmor)
		{
			EnsureReferences();
			SetBarFill(_hpFill, GetRatio(hp, maxHp));
			SetBarFill(_armorFill, GetRatio(armor, maxArmor));
			SetText(_hpText, FormatValue(hp, maxHp));
			SetText(_armorText, FormatValue(armor, maxArmor));
		}

		void Awake()
		{
			AutoBindReferences();
		}

#if UNITY_EDITOR
		void OnValidate()
		{
			AutoBindReferences();
		}
#endif

		void EnsureReferences()
		{
			AutoBindReferences();

			if (HasRequiredReferences())
			{
				EnsureRendererDefaults();
				return;
			}

			EnsureFallbackVisuals();
		}

		void AutoBindReferences()
		{
			if (_hpTrack == null)
				_hpTrack = FindSpriteRenderer("HpBarTrack");
			if (_hpFill == null)
				_hpFill = FindSpriteRenderer("HpBarFill");
			if (_armorTrack == null)
				_armorTrack = FindSpriteRenderer("ArmorBarTrack");
			if (_armorFill == null)
				_armorFill = FindSpriteRenderer("ArmorBarFill");
			if (_hpText == null)
				_hpText = FindTextMesh("HpBarText");
			if (_armorText == null)
				_armorText = FindTextMesh("ArmorBarText");
		}

		bool HasRequiredReferences()
		{
			return _hpTrack != null
				&& _hpFill != null
				&& _armorTrack != null
				&& _armorFill != null
				&& _hpText != null
				&& _armorText != null;
		}

		void EnsureRendererDefaults()
		{
			EnsureSprite(_hpTrack, BackgroundSortingOrder);
			EnsureSprite(_hpFill, FillSortingOrder);
			EnsureSprite(_armorTrack, BackgroundSortingOrder);
			EnsureSprite(_armorFill, FillSortingOrder);
			EnsureTextSortingOrder(_hpText);
			EnsureTextSortingOrder(_armorText);
		}

		SpriteRenderer FindSpriteRenderer(string objectName)
		{
			Transform child = transform.Find(objectName);
			return child != null ? child.GetComponent<SpriteRenderer>() : null;
		}

		TextMesh FindTextMesh(string objectName)
		{
			Transform child = transform.Find(objectName);
			return child != null ? child.GetComponent<TextMesh>() : null;
		}

		void EnsureFallbackVisuals()
		{
			float hpY = (BarHeight + BarGap) * 0.5f;
			float armorY = -hpY;
			Vector2 trackSize = new Vector2(BarWidth + TrackPadding * 2f, BarHeight + TrackPadding * 2f);
			Color trackColor = new Color(0.02f, 0.025f, 0.03f, 0.82f);

			_hpTrack = EnsureBarRenderer("HpBarTrack", hpY, trackSize, trackColor, BackgroundSortingOrder);
			_hpFill = EnsureBarRenderer("HpBarFill", hpY, new Vector2(BarWidth, BarHeight), new Color(0.82f, 0.18f, 0.16f, 1f), FillSortingOrder);
			_armorTrack = EnsureBarRenderer("ArmorBarTrack", armorY, trackSize, trackColor, BackgroundSortingOrder);
			_armorFill = EnsureBarRenderer("ArmorBarFill", armorY, new Vector2(BarWidth, BarHeight), new Color(0.35f, 0.68f, 1f, 1f), FillSortingOrder);
			_hpText = EnsureText("HpBarText", hpY);
			_armorText = EnsureText("ArmorBarText", armorY);
		}

		SpriteRenderer EnsureBarRenderer(string objectName, float localY, Vector2 size, Color color, int sortingOrder)
		{
			Transform child = transform.Find(objectName);
			if (child == null)
			{
				GameObject childObject = new GameObject(objectName);
				childObject.transform.SetParent(transform, false);
				child = childObject.transform;
			}

			child.localPosition = new Vector3(0f, localY, 0f);
			child.localScale = new Vector3(size.x, size.y, 1f);

			SpriteRenderer renderer = child.GetComponent<SpriteRenderer>();
			if (renderer == null)
			{
				renderer = child.gameObject.AddComponent<SpriteRenderer>();
				renderer.color = color;
			}

			EnsureSprite(renderer, sortingOrder);
			return renderer;
		}

		TextMesh EnsureText(string objectName, float localY)
		{
			Transform child = transform.Find(objectName);
			if (child == null)
			{
				GameObject childObject = new GameObject(objectName);
				childObject.transform.SetParent(transform, false);
				child = childObject.transform;
			}

			child.localPosition = new Vector3(0f, localY - 0.005f, 0f);

			TextMesh textMesh = child.GetComponent<TextMesh>();
			if (textMesh == null)
				textMesh = child.gameObject.AddComponent<TextMesh>();

			textMesh.anchor = TextAnchor.MiddleCenter;
			textMesh.alignment = TextAlignment.Center;
			textMesh.characterSize = 0.07f;
			textMesh.fontSize = 28;
			textMesh.color = Color.white;

			MeshRenderer renderer = child.GetComponent<MeshRenderer>();
			if (renderer != null)
				renderer.sortingOrder = TextSortingOrder;

			return textMesh;
		}

		static void EnsureSprite(SpriteRenderer renderer, int sortingOrder)
		{
			if (renderer == null)
				return;

			if (renderer.sprite == null)
				renderer.sprite = GetBarSprite();

			renderer.sortingOrder = sortingOrder;
		}

		static void EnsureTextSortingOrder(TextMesh textMesh)
		{
			if (textMesh == null)
				return;

			MeshRenderer renderer = textMesh.GetComponent<MeshRenderer>();
			if (renderer != null)
				renderer.sortingOrder = TextSortingOrder;
		}

		static void SetBarFill(SpriteRenderer fill, float ratio)
		{
			if (fill == null)
				return;

			ratio = Mathf.Clamp01(ratio);
			fill.transform.localScale = new Vector3(BarWidth * ratio, BarHeight, 1f);
			fill.transform.localPosition = new Vector3((-BarWidth + BarWidth * ratio) * 0.5f, fill.transform.localPosition.y, 0f);
		}

		static float GetRatio(int value, int maxValue)
		{
			if (maxValue <= 0)
				return value > 0 ? 1f : 0f;

			return (float)value / maxValue;
		}

		static void SetText(TextMesh textMesh, string value)
		{
			if (textMesh != null)
				textMesh.text = value;
		}

		static string FormatValue(int value, int maxValue)
		{
			return maxValue > 0 ? $"{value}/{maxValue}" : value.ToString();
		}

		static Sprite GetBarSprite()
		{
			if (_barSprite != null)
				return _barSprite;

			Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
			texture.name = "Runtime_PawnStatusWorldUISprite";
			texture.SetPixel(0, 0, Color.white);
			texture.Apply();

			_barSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
			return _barSprite;
		}
	}
}
