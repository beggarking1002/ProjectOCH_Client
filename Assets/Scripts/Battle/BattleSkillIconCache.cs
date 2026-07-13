using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
#endif

namespace Battle
{
	public static class BattleSkillIconCache
	{
		static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>(System.StringComparer.OrdinalIgnoreCase);
		static readonly Dictionary<string, AsyncOperationHandle<Sprite>> Handles = new Dictionary<string, AsyncOperationHandle<Sprite>>(System.StringComparer.OrdinalIgnoreCase);

		public static async Task<Sprite> LoadAsync(string iconKey)
		{
			if (string.IsNullOrWhiteSpace(iconKey))
				return null;

			if (Sprites.TryGetValue(iconKey, out Sprite cached))
				return cached;

			AsyncOperationHandle<Sprite> handle = Addressables.LoadAssetAsync<Sprite>(iconKey);
			await handle.Task;
			if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null)
			{
				Sprites[iconKey] = handle.Result;
				Handles[iconKey] = handle;
				return handle.Result;
			}

			if (handle.IsValid())
				Addressables.Release(handle);

#if UNITY_EDITOR
			Sprite editorSprite = LoadEditorSprite(iconKey);
			if (editorSprite != null)
			{
				Sprites[iconKey] = editorSprite;
				return editorSprite;
			}
#endif
			Debug.LogWarning($"Failed to load skill icon. iconKey={iconKey}");
			return null;
		}

#if UNITY_EDITOR
		static Sprite LoadEditorSprite(string iconKey)
		{
			string[] guids = AssetDatabase.FindAssets("t:Sprite", new[] { "Assets/@Resources/Art/SkillIcon" });
			foreach (string guid in guids)
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				string fileName = Path.GetFileNameWithoutExtension(path);
				string normalizedFileName = fileName.Replace("_", string.Empty).ToLowerInvariant();
				string normalizedIconKey = iconKey.Replace("icon_", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
				if (normalizedFileName.EndsWith(normalizedIconKey) || normalizedIconKey.EndsWith(normalizedFileName))
					return AssetDatabase.LoadAssetAtPath<Sprite>(path);
			}

			return null;
		}
#endif
	}
}
