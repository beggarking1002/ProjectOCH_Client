using System.IO;
using App;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

internal static class HoverPawnInfoPrefabSetup
{
    const string HoverPrefabPath = "Assets/@Resources/Prefab/UI/HoverPawnInfo.prefab";
    const string BattlePrefabPath = "Assets/@Resources/Prefab/UI/BattleSceneUI.prefab";
    const int SlotCount = 10;

    [InitializeOnLoadMethod]
    static void SetupOnReload()
    {
        EditorApplication.delayCall += Setup;
    }

    [MenuItem("Tools/Project OCH/UI/Setup Hover Pawn Info Prefab")]
    public static void Setup()
    {
        string directory = Path.GetDirectoryName(HoverPrefabPath);
        if (Directory.Exists(directory) == false)
            Directory.CreateDirectory(directory);

        bool hadExistingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HoverPrefabPath) != null;
        GameObject root = hadExistingPrefab
            ? PrefabUtility.LoadPrefabContents(HoverPrefabPath)
            : new GameObject("HoverPawnInfo", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
        try
        {
            Configure(root);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, HoverPrefabPath);
            AttachToBattleUi(prefab);
        }
        finally
        {
            if (hadExistingPrefab)
                PrefabUtility.UnloadPrefabContents(root);
            else
                Object.DestroyImmediate(root);
        }
    }

    static void Configure(GameObject root)
    {
        root.name = "HoverPawnInfo";
        RectTransform panel = root.GetComponent<RectTransform>();
        Image background = root.GetComponent<Image>();
        Outline outline = root.GetComponent<Outline>();
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = Vector2.zero;
        panel.sizeDelta = new Vector2(286f, 250f);
        background.color = new Color(0.10f, 0.075f, 0.035f, 0.94f);
        background.raycastTarget = false;
        outline.effectColor = new Color(0.72f, 0.59f, 0.31f, 0.78f);
        outline.effectDistance = new Vector2(1f, -1f);

        Text title = Text(root.transform, "Title", 16, TextAnchor.MiddleLeft, new Color(0.97f, 0.84f, 0.48f, 1f));
        title.text = "ALLY  ·  PAWN";
        title.rectTransform.anchorMin = new Vector2(0f, 0.84f);
        title.rectTransform.anchorMax = Vector2.one;
        title.rectTransform.offsetMin = new Vector2(12f, 2f);
        title.rectTransform.offsetMax = new Vector2(-12f, -6f);

        RectTransform stats = Rect(root.transform, "Stats");
        stats.anchorMin = new Vector2(0f, 0.30f);
        stats.anchorMax = new Vector2(1f, 0.84f);
        stats.offsetMin = new Vector2(10f, 0f);
        stats.offsetMax = new Vector2(-10f, -2f);
        for (int index = 0; index < SlotCount; index++)
            ConfigureStatSlot(stats, index);

        Text body = Text(root.transform, "Body", 12, TextAnchor.UpperLeft, new Color(0.92f, 0.90f, 0.82f, 1f));
        body.text = "ROLE  ·  (0, 0)\nResource: 0/0  ·  Ready";
        body.rectTransform.anchorMin = Vector2.zero;
        body.rectTransform.anchorMax = new Vector2(1f, 0.30f);
        body.rectTransform.offsetMin = new Vector2(12f, 10f);
        body.rectTransform.offsetMax = new Vector2(-12f, -2f);
        root.SetActive(false);
    }

    static void ConfigureStatSlot(RectTransform parent, int index)
    {
        RectTransform slot = Rect(parent, $"Stat_{index}");
        int column = index % 3;
        int row = index / 3;
        slot.anchorMin = new Vector2(column / 3f, 1f - ((row + 1) / 4f));
        slot.anchorMax = new Vector2((column + 1) / 3f, 1f - (row / 4f));
        slot.offsetMin = Vector2.one;
        slot.offsetMax = -Vector2.one;

        RectTransform iconRect = Rect(slot, "Icon");
        Image icon = iconRect.GetComponent<Image>() ?? iconRect.gameObject.AddComponent<Image>();
        if (iconRect.GetComponent<CanvasRenderer>() == null)
            iconRect.gameObject.AddComponent<CanvasRenderer>();
        icon.enabled = false;
        icon.raycastTarget = false;
        icon.preserveAspect = true;
        iconRect.anchorMin = new Vector2(0f, 0.15f);
        iconRect.anchorMax = new Vector2(0f, 0.85f);
        iconRect.sizeDelta = new Vector2(25f, 25f);
        iconRect.anchoredPosition = new Vector2(13f, 0f);

        Text value = Text(slot, "Value", 11, TextAnchor.MiddleLeft, new Color(0.96f, 0.92f, 0.78f, 1f));
        value.text = "0/0";
        value.rectTransform.anchorMin = Vector2.zero;
        value.rectTransform.anchorMax = Vector2.one;
        value.rectTransform.offsetMin = new Vector2(29f, 0f);
        value.rectTransform.offsetMax = Vector2.zero;
    }

    static Text Text(Transform parent, string name, int size, TextAnchor alignment, Color color)
    {
        RectTransform rect = Rect(parent, name);
        Text text = rect.GetComponent<Text>() ?? rect.gameObject.AddComponent<Text>();
        if (rect.GetComponent<CanvasRenderer>() == null)
            rect.gameObject.AddComponent<CanvasRenderer>();
        text.font = GameRoot.UiFont;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }

    static RectTransform Rect(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child != null)
            return child as RectTransform;
        GameObject childObject = new GameObject(name, typeof(RectTransform));
        childObject.layer = parent.gameObject.layer;
        childObject.transform.SetParent(parent, false);
        return childObject.GetComponent<RectTransform>();
    }

    static void AttachToBattleUi(GameObject hoverPrefab)
    {
        GameObject battle = PrefabUtility.LoadPrefabContents(BattlePrefabPath);
        try
        {
            Transform current = battle.transform.Find("HoverPawnInfo");
            if (current != null)
                Object.DestroyImmediate(current.gameObject);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(hoverPrefab, battle.transform);
            instance.name = "HoverPawnInfo";
            instance.transform.SetAsLastSibling();
            PrefabUtility.SaveAsPrefabAsset(battle, BattlePrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(battle);
        }
    }
}
