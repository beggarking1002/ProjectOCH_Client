using System.IO;
using Battle;
using UnityEditor;
using UnityEngine;

internal static class PawnStatusWorldUIPrefabSetup
{
    private const string StatusPrefabPath = "Assets/@Resources/Prefab/UI/PawnStatusWorldUI.prefab";
    private const string PawnPrefabFolder = "Assets/@Resources/Prefab/Pawn";
    private const string StatusObjectName = "PawnStatusWorldUI";
    private const int BackgroundSortingOrder = 38;
    private const int FillSortingOrder = 39;
    private const int TextSortingOrder = 40;
    private const float BarWidth = 0.9f;
    private const float BarHeight = 0.12f;
    private const float TrackPadding = 0.018f;
    private const float BarGap = 0.03f;

    [InitializeOnLoadMethod]
    private static void SetupOnReload()
    {
        EditorApplication.delayCall += () =>
        {
            GameObject statusPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(StatusPrefabPath);
            if (HasConfiguredStatusPrefab(statusPrefab) && AllPawnPrefabsUseStatusPrefab(statusPrefab))
                return;

            SetupPawnStatusWorldUI();
        };
    }

    [MenuItem("Tools/Project OCH/UI/Setup Pawn Status World UI")]
    public static void SetupPawnStatusWorldUI()
    {
        GameObject statusPrefab = EnsureStatusPrefab();
        if (statusPrefab == null)
            return;

        string[] pawnPrefabGuids = AssetDatabase.FindAssets("Pawn_ t:Prefab", new[] { PawnPrefabFolder });
        foreach (string guid in pawnPrefabGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            AttachStatusPrefabToPawn(path, statusPrefab);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Pawn status world UI prefab setup complete.");
    }

    private static bool HasConfiguredStatusPrefab(GameObject prefab)
    {
        return prefab != null
            && prefab.GetComponent<PawnStatusWorldUI>() != null
            && HasSpriteRenderer(prefab.transform, "HpBarTrack")
            && HasSpriteRenderer(prefab.transform, "HpBarFill")
            && HasTextMesh(prefab.transform, "HpBarText")
            && HasSpriteRenderer(prefab.transform, "ArmorBarTrack")
            && HasSpriteRenderer(prefab.transform, "ArmorBarFill")
            && HasTextMesh(prefab.transform, "ArmorBarText");
    }

    private static bool AllPawnPrefabsUseStatusPrefab(GameObject statusPrefab)
    {
        if (statusPrefab == null)
            return false;

        string[] pawnPrefabGuids = AssetDatabase.FindAssets("Pawn_ t:Prefab", new[] { PawnPrefabFolder });
        if (pawnPrefabGuids.Length == 0)
            return false;

        foreach (string guid in pawnPrefabGuids)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            PawnStatusWorldUI statusUi = prefab != null ? prefab.GetComponentInChildren<PawnStatusWorldUI>(true) : null;
            if (statusUi == null || IsStatusPrefabInstance(statusUi.gameObject, statusPrefab) == false)
                return false;
        }

        return true;
    }

    private static GameObject EnsureStatusPrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(StatusPrefabPath);
        string directory = Path.GetDirectoryName(StatusPrefabPath);
        if (string.IsNullOrWhiteSpace(directory) == false && Directory.Exists(directory) == false)
            Directory.CreateDirectory(directory);

        if (existing != null)
        {
            GameObject loadedRoot = PrefabUtility.LoadPrefabContents(StatusPrefabPath);
            if (loadedRoot == null)
                return existing;

            try
            {
                ConfigureStatusInstance(loadedRoot);
                return PrefabUtility.SaveAsPrefabAsset(loadedRoot, StatusPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(loadedRoot);
            }
        }

        GameObject root = new GameObject(StatusObjectName);
        ConfigureStatusInstance(root);
        GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(root, StatusPrefabPath);
        Object.DestroyImmediate(root);

        if (savedPrefab == null)
            Debug.LogError($"Failed to create pawn status prefab: {StatusPrefabPath}");

        return savedPrefab;
    }

    private static void AttachStatusPrefabToPawn(string pawnPrefabPath, GameObject statusPrefab)
    {
        if (string.IsNullOrWhiteSpace(pawnPrefabPath) || statusPrefab == null)
            return;

        GameObject pawnRoot = PrefabUtility.LoadPrefabContents(pawnPrefabPath);
        if (pawnRoot == null)
            return;

        try
        {
            PawnStatusWorldUI existing = pawnRoot.GetComponentInChildren<PawnStatusWorldUI>(true);
            if (existing != null)
            {
                if (IsStatusPrefabInstance(existing.gameObject, statusPrefab))
                    return;

                Object.DestroyImmediate(existing.gameObject);
            }

            GameObject statusInstance = (GameObject)PrefabUtility.InstantiatePrefab(statusPrefab, pawnRoot.transform);
            statusInstance.name = StatusObjectName;
            statusInstance.transform.localPosition = Vector3.zero;
            statusInstance.transform.localRotation = Quaternion.identity;
            statusInstance.transform.localScale = Vector3.one;

            PrefabUtility.SaveAsPrefabAsset(pawnRoot, pawnPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(pawnRoot);
        }
    }

    private static bool IsStatusPrefabInstance(GameObject statusObject, GameObject statusPrefab)
    {
        if (statusObject == null || statusPrefab == null)
            return false;

        GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(statusObject);
        return source == statusPrefab || AssetDatabase.GetAssetPath(source) == StatusPrefabPath;
    }

    private static void ConfigureStatusInstance(GameObject root)
    {
        root.name = StatusObjectName;

        PawnStatusWorldUI statusUi = root.GetComponent<PawnStatusWorldUI>();
        if (statusUi == null)
            statusUi = root.AddComponent<PawnStatusWorldUI>();

        float hpY = (BarHeight + BarGap) * 0.5f;
        float armorY = -hpY;
        Vector3 trackScale = new Vector3(BarWidth + TrackPadding * 2f, BarHeight + TrackPadding * 2f, 1f);
        Vector3 fillScale = new Vector3(BarWidth, BarHeight, 1f);
        Color trackColor = new Color(0.02f, 0.025f, 0.03f, 0.82f);

        SpriteRenderer hpTrack = EnsureSpriteRenderer(root.transform, "HpBarTrack", hpY, trackScale, trackColor, BackgroundSortingOrder);
        SpriteRenderer hpFill = EnsureSpriteRenderer(root.transform, "HpBarFill", hpY, fillScale, new Color(0.82f, 0.18f, 0.16f, 1f), FillSortingOrder);
        TextMesh hpText = EnsureTextMesh(root.transform, "HpBarText", hpY - 0.005f);
        SpriteRenderer armorTrack = EnsureSpriteRenderer(root.transform, "ArmorBarTrack", armorY, trackScale, trackColor, BackgroundSortingOrder);
        SpriteRenderer armorFill = EnsureSpriteRenderer(root.transform, "ArmorBarFill", armorY, fillScale, new Color(0.35f, 0.68f, 1f, 1f), FillSortingOrder);
        TextMesh armorText = EnsureTextMesh(root.transform, "ArmorBarText", armorY - 0.005f);

        SerializedObject serializedUi = new SerializedObject(statusUi);
        SetObjectReference(serializedUi, "_hpTrack", hpTrack);
        SetObjectReference(serializedUi, "_hpFill", hpFill);
        SetObjectReference(serializedUi, "_armorTrack", armorTrack);
        SetObjectReference(serializedUi, "_armorFill", armorFill);
        SetObjectReference(serializedUi, "_hpText", hpText);
        SetObjectReference(serializedUi, "_armorText", armorText);
        serializedUi.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(root);
        EditorUtility.SetDirty(statusUi);
    }

    private static SpriteRenderer EnsureSpriteRenderer(Transform root, string objectName, float localY, Vector3 localScale, Color color, int sortingOrder)
    {
        Transform child = FindOrCreateChild(root, objectName);
        child.localPosition = new Vector3(0f, localY, 0f);
        child.localRotation = Quaternion.identity;
        child.localScale = localScale;

        SpriteRenderer spriteRenderer = child.GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
            spriteRenderer = child.gameObject.AddComponent<SpriteRenderer>();

        spriteRenderer.color = color;
        spriteRenderer.sortingOrder = sortingOrder;
        spriteRenderer.sprite = LoadDefaultBarSprite();
        EditorUtility.SetDirty(spriteRenderer);
        return spriteRenderer;
    }

    private static TextMesh EnsureTextMesh(Transform root, string objectName, float localY)
    {
        Transform child = FindOrCreateChild(root, objectName);
        child.localPosition = new Vector3(0f, localY, 0f);
        child.localRotation = Quaternion.identity;
        child.localScale = Vector3.one;

        TextMesh textMesh = child.GetComponent<TextMesh>();
        if (textMesh == null)
            textMesh = child.gameObject.AddComponent<TextMesh>();

        textMesh.text = "0/0";
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.characterSize = 0.07f;
        textMesh.fontSize = 28;
        textMesh.color = Color.white;

        MeshRenderer meshRenderer = child.GetComponent<MeshRenderer>();
        if (meshRenderer != null)
            meshRenderer.sortingOrder = TextSortingOrder;

        EditorUtility.SetDirty(textMesh);
        return textMesh;
    }

    private static Transform FindOrCreateChild(Transform root, string objectName)
    {
        Transform child = root.Find(objectName);
        if (child != null)
            return child;

        GameObject childObject = new GameObject(objectName);
        childObject.transform.SetParent(root, false);
        return childObject.transform;
    }

    private static bool HasSpriteRenderer(Transform root, string objectName)
    {
        Transform child = root.Find(objectName);
        return child != null && child.GetComponent<SpriteRenderer>() != null;
    }

    private static bool HasTextMesh(Transform root, string objectName)
    {
        Transform child = root.Find(objectName);
        return child != null && child.GetComponent<TextMesh>() != null;
    }

    private static void SetObjectReference(SerializedObject serializedObject, string propertyName, Object value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
            property.objectReferenceValue = value;
    }

    private static Sprite LoadDefaultBarSprite()
    {
        return AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
    }
}
