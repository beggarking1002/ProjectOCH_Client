using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

internal static class BattleUiPrefabSetup
{
    private const string BattleUiPrefabPath = "Assets/@Resources/Prefab/UI/BattleSceneUI.prefab";

    [InitializeOnLoadMethod]
    private static void SetupBattleUiPrefabOnReload()
    {
        EditorApplication.delayCall += () =>
        {
            if (HasRequiredPawnPanelNodes())
                return;

            SetupBattleUiPrefab();
        };
    }

    [MenuItem("Tools/Project OCH/UI/Setup Battle UI Prefab")]
    public static void SetupBattleUiPrefab()
    {
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(BattleUiPrefabPath);
        if (prefabRoot == null)
        {
            Debug.LogError($"Failed to load battle UI prefab: {BattleUiPrefabPath}");
            return;
        }

        try
        {
            EnsurePawnPanel(prefabRoot.transform, "Left_SelectedPawnPanel", "SelectedPawn_StateText", "SelectedPawn_StatusBars");
            EnsurePawnPanel(prefabRoot.transform, "Right_EnemyPawnPanel", "EnemyPawn_StateText", "EnemyPawn_StatusBars");

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, BattleUiPrefabPath);
            Debug.Log($"Battle UI prefab setup complete: {BattleUiPrefabPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    private static bool HasRequiredPawnPanelNodes()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BattleUiPrefabPath);
        if (prefab == null)
            return false;

        return FindDeepChild(prefab.transform, "SelectedPawn_StatusBars") != null
            && FindDeepChild(prefab.transform, "EnemyPawn_StatusBars") != null
            && FindDeepChild(prefab.transform, "SelectedPawn_StateText") != null
            && FindDeepChild(prefab.transform, "EnemyPawn_StateText") != null;
    }

    private static void EnsurePawnPanel(Transform root, string panelName, string textName, string barsName)
    {
        Transform panel = FindDeepChild(root, panelName);
        if (panel == null)
        {
            Debug.LogWarning($"Missing battle UI panel in prefab: {panelName}");
            return;
        }

        EnsurePanelText(panel, textName);

        RectTransform bars = EnsureRectTransform(panel, barsName);
        bars.anchorMin = new Vector2(0f, 0f);
        bars.anchorMax = new Vector2(1f, 0f);
        bars.pivot = new Vector2(0.5f, 0f);
        bars.offsetMin = new Vector2(8f, 8f);
        bars.offsetMax = new Vector2(-8f, 52f);

        EnsureBar(bars, "HpBar", 24f, new Color(0.02f, 0.025f, 0.03f, 0.78f), new Color(0.82f, 0.18f, 0.16f, 1f));
        EnsureBar(bars, "ArmorBar", 4f, new Color(0.02f, 0.025f, 0.03f, 0.78f), new Color(0.35f, 0.68f, 1f, 1f));
    }

    private static void EnsurePanelText(Transform panel, string textName)
    {
        RectTransform textRect = EnsureRectTransform(panel, textName);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.pivot = new Vector2(0f, 1f);
        textRect.offsetMin = new Vector2(8f, 58f);
        textRect.offsetMax = new Vector2(-8f, -6f);

        Text text = textRect.GetComponent<Text>();
        if (text == null)
            text = textRect.gameObject.AddComponent<Text>();

        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 13;
        text.color = Color.white;
        text.alignment = TextAnchor.UpperLeft;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.raycastTarget = false;
    }

    private static void EnsureBar(RectTransform parent, string name, float bottom, Color trackColor, Color fillColor)
    {
        RectTransform track = EnsureRectTransform(parent, name);
        track.anchorMin = new Vector2(0f, 0f);
        track.anchorMax = new Vector2(1f, 0f);
        track.pivot = new Vector2(0.5f, 0f);
        track.offsetMin = new Vector2(0f, bottom);
        track.offsetMax = new Vector2(0f, bottom + 12f);

        Image trackImage = track.GetComponent<Image>();
        if (trackImage == null)
            trackImage = track.gameObject.AddComponent<Image>();

        trackImage.color = trackColor;
        trackImage.raycastTarget = false;

        RectTransform fill = EnsureRectTransform(track, "Fill");
        fill.anchorMin = new Vector2(0f, 0f);
        fill.anchorMax = new Vector2(1f, 1f);
        fill.offsetMin = new Vector2(1f, 1f);
        fill.offsetMax = new Vector2(-1f, -1f);

        Image fillImage = fill.GetComponent<Image>();
        if (fillImage == null)
            fillImage = fill.gameObject.AddComponent<Image>();

        fillImage.color = fillColor;
        fillImage.raycastTarget = false;
    }

    private static RectTransform EnsureRectTransform(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child == null)
        {
            GameObject childObject = new GameObject(name);
            childObject.layer = parent.gameObject.layer;
            childObject.transform.SetParent(parent, false);
            return childObject.AddComponent<RectTransform>();
        }

        RectTransform rect = child.GetComponent<RectTransform>();
        if (rect == null)
            rect = child.gameObject.AddComponent<RectTransform>();

        return rect;
    }

    private static Transform FindDeepChild(Transform parent, string childName)
    {
        if (parent == null)
            return null;

        if (parent.name == childName)
            return parent;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform result = FindDeepChild(parent.GetChild(i), childName);
            if (result != null)
                return result;
        }

        return null;
    }
}
