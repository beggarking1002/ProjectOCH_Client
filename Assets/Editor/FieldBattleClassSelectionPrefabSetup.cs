using System.IO;
using App;
using Field;
using Protocol;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.UI;

internal static class FieldBattleClassSelectionPrefabSetup
{
	struct OptionDefinition
	{
		public readonly PawnClass PawnClass;
		public readonly string Label;
		public readonly string EmblemPath;
		public OptionDefinition(PawnClass pawnClass, string label, string emblemPath)
		{
			PawnClass = pawnClass;
			Label = label;
			EmblemPath = emblemPath;
		}
	}

	const string PrefabPath = "Assets/@Resources/Prefab/UI/FieldBattleClassSelectionUI.prefab";
	const string Address = "FieldBattleClassSelectionUI";
	const string UiAtlasPath = "Assets/@Resources/Art/UI/UI_3.png";
	const string EmblemPath = "Assets/@Resources/Art/Emblem/";
	const string SubclassEmblemPath = EmblemPath + "Subclass/";
	static readonly Color Gold = new Color(0.97f, 0.84f, 0.48f, 1f);
	static readonly Color Cream = new Color(0.92f, 0.90f, 0.82f, 1f);
	static readonly Color NormalColor = new Color(0.18f, 0.125f, 0.055f, 0.96f);
	static readonly OptionDefinition[] SuenOptions =
	{
		new OptionDefinition(PawnClass.SuenAxeSword, "AXE SWORD", SubclassEmblemPath + "Emblem_Suen_AxeSword.png"),
		new OptionDefinition(PawnClass.SuenParvis, "PARVIS", SubclassEmblemPath + "Emblem_Suen_Parvis.png"),
	};
	static readonly OptionDefinition[] BeigeOptions =
	{
		new OptionDefinition(PawnClass.BeigeIce, "ICE", SubclassEmblemPath + "Emblem_Beige_Ice.png"),
		new OptionDefinition(PawnClass.BeigeFire, "FIRE", SubclassEmblemPath + "Emblem_Beige_Fire.png"),
	};
	static readonly OptionDefinition[] AlenOptions =
	{
		new OptionDefinition(PawnClass.AlenSpear, "SPEAR", SubclassEmblemPath + "Emblem_Alen_Spear.png"),
		new OptionDefinition(PawnClass.AlenSwordShield, "SWORD & SHIELD", SubclassEmblemPath + "Emblem_Alen_SwordShield.png"),
	};
	static readonly OptionDefinition[] ZillianOptions =
	{
		new OptionDefinition(PawnClass.ZillianLongbow, "LONGBOW", SubclassEmblemPath + "Emblem_Zillian_Longbow.png"),
		new OptionDefinition(PawnClass.ZillianMace, "MACE", SubclassEmblemPath + "Emblem_Zillian_Mace.png"),
	};

	[InitializeOnLoadMethod]
	static void SetupOnReload()
	{
		EditorApplication.delayCall += Setup;
	}

	[MenuItem("Tools/Project OCH/UI/Setup Field Battle Class Selection UI Prefab")]
	public static void Setup()
	{
		string directory = Path.GetDirectoryName(PrefabPath);
		if (Directory.Exists(directory) == false)
			Directory.CreateDirectory(directory);

		bool hadPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;
		GameObject root = hadPrefab
			? PrefabUtility.LoadPrefabContents(PrefabPath)
			: new GameObject("Canvas_FieldBattleClassSelectionUI", typeof(RectTransform));
		try
		{
			Configure(root);
			PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
		}
		finally
		{
			if (hadPrefab)
				PrefabUtility.UnloadPrefabContents(root);
			else
				Object.DestroyImmediate(root);
		}

		RegisterAddressable();
		AssetDatabase.SaveAssets();
	}

	static void Configure(GameObject root)
	{
		root.name = "Canvas_FieldBattleClassSelectionUI";
		ClearChildren(root.transform);
		Canvas canvas = GetOrAdd<Canvas>(root);
		CanvasScaler scaler = GetOrAdd<CanvasScaler>(root);
		GetOrAdd<GraphicRaycaster>(root);
		FieldBattleClassSelectionView view = GetOrAdd<FieldBattleClassSelectionView>(root);
		canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		canvas.sortingOrder = 1250;
		scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
		scaler.referenceResolution = new Vector2(1280f, 720f);

		GameObject dimmer = Create("Dimmer", root.transform, typeof(Image));
		Stretch(dimmer.GetComponent<RectTransform>());
		dimmer.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.62f);

		GameObject panel = Create("Panel", dimmer.transform, typeof(Image), typeof(VerticalLayoutGroup));
		RectTransform panelRect = panel.GetComponent<RectTransform>();
		panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
		panelRect.pivot = new Vector2(0.5f, 0.5f);
		panelRect.sizeDelta = new Vector2(800f, 610f);
		Image panelImage = panel.GetComponent<Image>();
		panelImage.sprite = FindSprite(UiAtlasPath, "UI_3_0");
		panelImage.type = Image.Type.Sliced;
		panelImage.color = Color.white;
		VerticalLayoutGroup panelLayout = panel.GetComponent<VerticalLayoutGroup>();
		panelLayout.padding = new RectOffset(42, 42, 34, 34);
		panelLayout.spacing = 10;
		panelLayout.childControlWidth = true;
		panelLayout.childControlHeight = true;
		panelLayout.childForceExpandWidth = true;
		panelLayout.childForceExpandHeight = false;

		Text title = Text("Title", panel.transform, 30, TextAnchor.MiddleCenter, Gold);
		title.text = "PVP CLASS SELECTION";
		Layout(title.gameObject, 52f);
		Text status = Text("Status", panel.transform, 18, TextAnchor.MiddleCenter, Cream);
		status.text = "Choose one class from each group.";
		Layout(status.gameObject, 42f);
		GameObject options = Create("Options", panel.transform, typeof(HorizontalLayoutGroup));
		HorizontalLayoutGroup optionsLayout = options.GetComponent<HorizontalLayoutGroup>();
		optionsLayout.spacing = 10;
		optionsLayout.childControlWidth = true;
		optionsLayout.childControlHeight = true;
		optionsLayout.childForceExpandWidth = true;
		optionsLayout.childForceExpandHeight = false;
		CreateColumn("SuenColumn", "SUEN", EmblemPath + "Emblem_Suen.png", SuenOptions, options.transform);
		CreateColumn("BeigeColumn", "BEIGE", EmblemPath + "Emblem_Beige.png", BeigeOptions, options.transform);
		CreateColumn("AlenColumn", "ALEN", EmblemPath + "Emblem_Alen.png", AlenOptions, options.transform);
		CreateColumn("ZillianColumn", "ZILLIAN", EmblemPath + "Emblem_Zillian.png", ZillianOptions, options.transform);
		Layout(options, 352f);
		Button submit = Button("Submit", panel.transform, "CONFIRM", out Text submitText);
		Layout(submit.gameObject, 58f);

		view.Configure(canvas, panel, status, options.transform, submit, submitText);
		GameRoot.ApplyUiFont(root);
		root.SetActive(false);
	}

	static void CreateColumn(string name, string label, string emblemPath, OptionDefinition[] definitions, Transform parent)
	{
		GameObject column = Create(name, parent, typeof(VerticalLayoutGroup), typeof(LayoutElement));
		VerticalLayoutGroup layout = column.GetComponent<VerticalLayoutGroup>();
		layout.spacing = 8;
		layout.childAlignment = TextAnchor.UpperCenter;
		layout.childControlWidth = true;
		layout.childControlHeight = true;
		layout.childForceExpandWidth = true;
		layout.childForceExpandHeight = false;
		LayoutElement columnLayout = column.GetComponent<LayoutElement>();
		columnLayout.preferredWidth = 166f;
		GameObject emblemObject = Create("Emblem", column.transform, typeof(Image));
		Image emblem = emblemObject.GetComponent<Image>();
		emblem.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(emblemPath);
		emblem.preserveAspect = true;
		emblem.raycastTarget = false;
		Layout(emblemObject, 76f);
		Text columnLabel = Text("Label", column.transform, 20, TextAnchor.MiddleCenter, Gold);
		columnLabel.text = label;
		Layout(columnLabel.gameObject, 30f);
		for (int index = 0; index < definitions.Length; index++)
			CreateOptionCard(column.transform, definitions[index]);
	}

	static void CreateOptionCard(Transform parent, OptionDefinition definition)
	{
		GameObject card = Create(definition.PawnClass.ToString(), parent, typeof(Image), typeof(Button));
		Image background = card.GetComponent<Image>();
		background.sprite = FindSprite(UiAtlasPath, "UI_3_6");
		background.type = Image.Type.Sliced;
		background.color = NormalColor;
		Button button = card.GetComponent<Button>();
		button.targetGraphic = background;
		Layout(card, 145f);

		GameObject emblemObject = Create("Emblem", card.transform, typeof(Image));
		Image emblem = emblemObject.GetComponent<Image>();
		emblem.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(definition.EmblemPath);
		emblem.preserveAspect = true;
		emblem.raycastTarget = false;
		RectTransform emblemRect = emblem.rectTransform;
		emblemRect.anchorMin = new Vector2(0.16f, 0.30f);
		emblemRect.anchorMax = new Vector2(0.84f, 0.88f);
		emblemRect.offsetMin = Vector2.zero;
		emblemRect.offsetMax = Vector2.zero;

		Text text = Text("Text", card.transform, 15, TextAnchor.MiddleCenter, Cream);
		text.text = definition.Label;
		RectTransform textRect = text.rectTransform;
		textRect.anchorMin = new Vector2(0.06f, 0.04f);
		textRect.anchorMax = new Vector2(0.94f, 0.28f);
		textRect.offsetMin = Vector2.zero;
		textRect.offsetMax = Vector2.zero;
	}

	static void RegisterAddressable()
	{
		AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
		if (settings == null)
		{
			Debug.LogError("Addressable settings are required to register FieldBattleClassSelectionUI.");
			return;
		}

		AddressableAssetGroup group = settings.FindGroup("UI") ?? settings.DefaultGroup;
		AddressableAssetEntry entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(PrefabPath), group);
		entry.address = Address;
		entry.SetLabel("UI", true, true);
		settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true);
	}

	static T GetOrAdd<T>(GameObject gameObject) where T : Component
	{
		T component = gameObject.GetComponent<T>();
		return component != null ? component : gameObject.AddComponent<T>();
	}

	static GameObject Create(string name, Transform parent, params System.Type[] components)
	{
		GameObject result = new GameObject(name, components);
		result.transform.SetParent(parent, false);
		return result;
	}

	static Text Text(string name, Transform parent, int fontSize, TextAnchor alignment, Color color)
	{
		GameObject go = Create(name, parent, typeof(Text));
		Text text = go.GetComponent<Text>();
		text.font = GameRoot.UiFont;
		text.fontSize = fontSize;
		text.alignment = alignment;
		text.color = color;
		text.raycastTarget = false;
		return text;
	}

	static Button Button(string name, Transform parent, string label, out Text labelText)
	{
		GameObject go = Create(name, parent, typeof(Image), typeof(Button));
		Image image = go.GetComponent<Image>();
		image.sprite = FindSprite(UiAtlasPath, "UI_3_14");
		image.type = Image.Type.Sliced;
		image.color = Color.white;
		Button button = go.GetComponent<Button>();
		button.targetGraphic = image;
		labelText = Text("Text", go.transform, 20, TextAnchor.MiddleCenter, Gold);
		labelText.text = label;
		Stretch(labelText.rectTransform);
		return button;
	}

	static Sprite FindSprite(string path, string spriteName)
	{
		Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
		for (int index = 0; index < assets.Length; index++)
		{
			if (assets[index] is Sprite sprite && sprite.name == spriteName)
				return sprite;
		}
		return null;
	}

	static void Layout(GameObject gameObject, float height)
	{
		LayoutElement element = gameObject.AddComponent<LayoutElement>();
		element.preferredHeight = height;
	}

	static void Stretch(RectTransform rect)
	{
		rect.anchorMin = Vector2.zero;
		rect.anchorMax = Vector2.one;
		rect.offsetMin = Vector2.zero;
		rect.offsetMax = Vector2.zero;
	}

	static void ClearChildren(Transform transform)
	{
		for (int index = transform.childCount - 1; index >= 0; index--)
			Object.DestroyImmediate(transform.GetChild(index).gameObject);
	}
}
