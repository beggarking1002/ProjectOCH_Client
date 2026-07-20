using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

internal static class ProjectFontSetup
{
	const string SourceFontPath = "Assets/@Resources/Font/Maplestory Light.ttf";
	const string TmpFontPath = "Assets/@Resources/Font/Maplestory Light SDF.asset";
	const string UiPrefabRoot = "Assets/@Resources/Prefab/UI";
	const string SceneRoot = "Assets/Scenes";

	[MenuItem("Tools/Project OCH/UI/Apply Maplestory Light Font")]
	public static void ApplyMaplestoryLight()
	{
		Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
		if (sourceFont == null)
		{
			Debug.LogError($"Project font is missing: {SourceFontPath}");
			return;
		}

		TMP_FontAsset tmpFont = LoadOrCreateTmpFont(sourceFont);
		if (tmpFont == null)
			return;

		ApplyToUiPrefabs(sourceFont);
		ApplyToProjectScenes(tmpFont);
		SetTmpDefaultFont(tmpFont);
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		Debug.Log("Applied Maplestory Light to project UI fonts.");
	}

	static TMP_FontAsset LoadOrCreateTmpFont(Font sourceFont)
	{
		TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TmpFontPath);
		if (existing != null)
			return existing;

		TMP_FontAsset created = TMP_FontAsset.CreateFontAsset(sourceFont);
		if (created == null)
		{
			Debug.LogError("Failed to create the Maplestory Light TMP font asset.");
			return null;
		}

		AssetDatabase.CreateAsset(created, TmpFontPath);
		return created;
	}

	static void ApplyToUiPrefabs(Font font)
	{
		string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { UiPrefabRoot });
		foreach (string guid in guids)
		{
			string path = AssetDatabase.GUIDToAssetPath(guid);
			GameObject root = PrefabUtility.LoadPrefabContents(path);
			if (root == null)
				continue;

			try
			{
				foreach (Text text in root.GetComponentsInChildren<Text>(true))
					text.font = font;
				foreach (TextMesh textMesh in root.GetComponentsInChildren<TextMesh>(true))
					ApplyWorldTextFont(textMesh, font);

				PrefabUtility.SaveAsPrefabAsset(root, path);
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents(root);
			}
		}
	}

	static void ApplyWorldTextFont(TextMesh textMesh, Font font)
	{
		textMesh.font = font;
		MeshRenderer renderer = textMesh.GetComponent<MeshRenderer>();
		if (renderer != null)
			renderer.sharedMaterial = font.material;
	}

	static void ApplyToProjectScenes(TMP_FontAsset font)
	{
		string[] guids = AssetDatabase.FindAssets("t:Scene", new[] { SceneRoot });
		foreach (string guid in guids)
		{
			string path = AssetDatabase.GUIDToAssetPath(guid);
			var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
			foreach (TMP_Text text in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
				text.font = font;

			EditorSceneManager.SaveScene(scene);
		}
	}

	static void SetTmpDefaultFont(TMP_FontAsset font)
	{
		SerializedObject settings = new SerializedObject(TMP_Settings.instance);
		SerializedProperty defaultFont = settings.FindProperty("m_defaultFontAsset");
		defaultFont.objectReferenceValue = font;
		settings.ApplyModifiedPropertiesWithoutUndo();
	}
}
