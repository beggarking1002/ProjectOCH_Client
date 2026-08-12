using System.IO;
using App;
using Field;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.UI;

internal static class FieldBattleClassSelectionPrefabSetup
{
	const string PrefabPath = "Assets/@Resources/Prefab/UI/FieldBattleClassSelectionUI.prefab";
	const string Address = "FieldBattleClassSelectionUI";
	static readonly Color PanelColor = new Color(0.10f, 0.075f, 0.035f, 0.97f);
	static readonly Color Gold = new Color(0.97f, 0.84f, 0.48f, 1f);
	static readonly Color Cream = new Color(0.92f, 0.90f, 0.82f, 1f);
	static readonly Color NormalColor = new Color(0.18f, 0.125f, 0.055f, 0.96f);

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

		GameObject panel = Create("Panel", dimmer.transform, typeof(Image), typeof(Outline), typeof(VerticalLayoutGroup));
		RectTransform panelRect = panel.GetComponent<RectTransform>();
		panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
		panelRect.pivot = new Vector2(0.5f, 0.5f);
		panelRect.sizeDelta = new Vector2(780f, 550f);
		panel.GetComponent<Image>().color = PanelColor;
		Outline outline = panel.GetComponent<Outline>();
		outline.effectColor = new Color(0.72f, 0.59f, 0.31f, 0.9f);
		outline.effectDistance = new Vector2(2f, -2f);
		VerticalLayoutGroup panelLayout = panel.GetComponent<VerticalLayoutGroup>();
		panelLayout.padding = new RectOffset(32, 32, 24, 24);
		panelLayout.spacing = 12;
		panelLayout.childControlWidth = true;
		panelLayout.childControlHeight = true;
		panelLayout.childForceExpandWidth = true;
		panelLayout.childForceExpandHeight = false;

		Text title = Text("Title", panel.transform, 30, TextAnchor.MiddleCenter, Gold);
		title.text = "PvP CLASS SELECTION";
		Layout(title.gameObject, 52f);
		Text status = Text("Status", panel.transform, 18, TextAnchor.MiddleCenter, Cream);
		status.text = "Choose one class for each group.";
		Layout(status.gameObject, 46f);
		GameObject options = Create("Options", panel.transform, typeof(HorizontalLayoutGroup));
		HorizontalLayoutGroup optionsLayout = options.GetComponent<HorizontalLayoutGroup>();
		optionsLayout.spacing = 12;
		optionsLayout.childControlWidth = true;
		optionsLayout.childControlHeight = true;
		optionsLayout.childForceExpandWidth = true;
		optionsLayout.childForceExpandHeight = false;
		Layout(options, 264f);
		Button submit = Button("Submit", panel.transform, "CONFIRM", out Text submitText);
		Layout(submit.gameObject, 54f);

		view.Configure(canvas, panel, status, options.transform, submit, submitText);
		GameRoot.ApplyUiFont(root);
		root.SetActive(false);
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
		string guid = AssetDatabase.AssetPathToGUID(PrefabPath);
		AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, group);
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
		image.color = NormalColor;
		Button button = go.GetComponent<Button>();
		button.targetGraphic = image;
		labelText = Text("Text", go.transform, 20, TextAnchor.MiddleCenter, Gold);
		labelText.text = label;
		Stretch(labelText.rectTransform);
		return button;
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
