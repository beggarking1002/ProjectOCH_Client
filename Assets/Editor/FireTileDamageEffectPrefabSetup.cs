using Battle;
using UnityEditor;
using UnityEngine;

internal static class FireTileDamageEffectPrefabSetup
{
	const string PrefabPath = "Assets/@Resources/Prefab/Effect/FireTileDamageEffect.prefab";
	const string FireballSpritePath = "Assets/@Resources/Art/Projectile/fireball.png";

	[InitializeOnLoadMethod]
	static void SetupOnEditorReload()
	{
		EditorApplication.delayCall += () =>
		{
			if (HasVisibleFlameRenderers() == false)
				RebuildPrefab();
		};
	}

	[MenuItem("Tools/Project OCH/Effects/Rebuild Fire Tile Damage Prefab")]
	public static void RebuildPrefab()
	{
		Sprite[] sprites = LoadFireballSprites();
		if (sprites.Length == 0)
		{
			Debug.LogError($"Cannot create fire-tile effect prefab: no sprites found at {FireballSpritePath}");
			return;
		}

		GameObject root = new GameObject("FireTileDamageEffect");
		try
		{
			BattleFireTileDamageEffect effect = root.AddComponent<BattleFireTileDamageEffect>();
			Vector3[] positions =
			{
				new Vector3(0.28f, 0f, 0f),
				new Vector3(0.07f, 0.11f, 0f),
				new Vector3(-0.14f, 0.11f, 0f),
				new Vector3(-0.14f, -0.05f, 0f),
				new Vector3(-0.14f, -0.12f, 0f),
				new Vector3(0.07f, -0.11f, 0f),
			};
			Color outer = new Color(1f, 0.24f, 0.04f, 0.9f);
			Color inner = new Color(1f, 0.78f, 0.12f, 0.95f);

			for (int i = 0; i < positions.Length; i++)
			{
				bool isOuter = i % 2 == 0;
				GameObject flame = new GameObject(isOuter ? $"Flame_Outer_{i / 2 + 1:00}" : $"Flame_Inner_{i / 2 + 1:00}");
				flame.transform.SetParent(root.transform, false);
				flame.transform.localPosition = positions[i];
				flame.transform.localRotation = Quaternion.Euler(0f, 0f, i * 60f + 45f);
				flame.transform.localScale = isOuter ? new Vector3(0.22f, 0.34f, 1f) : new Vector3(0.18f, 0.28f, 1f);

				SpriteRenderer renderer = flame.AddComponent<SpriteRenderer>();
				renderer.sprite = sprites[i % sprites.Length];
				renderer.color = isOuter ? outer : inner;
				renderer.sortingOrder = 30;
			}

			PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
			Debug.Log($"Rebuilt fire-tile damage effect prefab: {PrefabPath}");
		}
		finally
		{
			Object.DestroyImmediate(root);
		}
	}

	static bool HasVisibleFlameRenderers()
	{
		GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
		if (prefab == null)
			return false;

		SpriteRenderer[] renderers = prefab.GetComponentsInChildren<SpriteRenderer>(true);
		if (renderers.Length < 6)
			return false;

		for (int i = 0; i < renderers.Length; i++)
		{
			if (renderers[i].sprite == null)
				return false;
		}

		return prefab.GetComponent<BattleFireTileDamageEffect>() != null;
	}

	static Sprite[] LoadFireballSprites()
	{
		Object[] assets = AssetDatabase.LoadAllAssetsAtPath(FireballSpritePath);
		System.Array.Sort(assets, (left, right) => string.CompareOrdinal(left.name, right.name));
		int count = 0;
		for (int i = 0; i < assets.Length; i++)
		{
			if (assets[i] is Sprite)
				count++;
		}

		Sprite[] sprites = new Sprite[count];
		int index = 0;
		for (int i = 0; i < assets.Length; i++)
		{
			if (assets[i] is Sprite sprite)
				sprites[index++] = sprite;
		}

		return sprites;
	}
}
