using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
#endif

namespace Battle
{
	[DisallowMultipleComponent]
	public sealed class BattleProjectilePresenter : MonoBehaviour
	{
		const int ProjectileSortingOrder = 45;
		const float MinimumFlightSeconds = 0.14f;
		const float MaximumFlightSeconds = 0.56f;
		const float TargetVerticalOffset = 0.14f;

		readonly struct ProjectileStyle
		{
			public readonly float UnitsPerSecond;
			public readonly float Scale;
			public readonly float SpinDegrees;

			public ProjectileStyle(float unitsPerSecond, float scale, float spinDegrees = 0f)
			{
				UnitsPerSecond = unitsPerSecond;
				Scale = scale;
				SpinDegrees = spinDegrees;
			}
		}

		static readonly Dictionary<string, ProjectileStyle> Styles = new Dictionary<string, ProjectileStyle>(System.StringComparer.OrdinalIgnoreCase)
		{
			{ "arrow", new ProjectileStyle(7.5f, 0.55f) },
			{ "dark_arrow", new ProjectileStyle(7.0f, 0.55f) },
			{ "fireball", new ProjectileStyle(6.5f, 0.58f) },
			{ "iceball", new ProjectileStyle(6.2f, 0.56f) },
			{ "throwing_axe", new ProjectileStyle(5.8f, 0.52f, 900f) },
		};

		readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>(System.StringComparer.OrdinalIgnoreCase);
		readonly Dictionary<string, AsyncOperationHandle<Sprite>> _handles = new Dictionary<string, AsyncOperationHandle<Sprite>>(System.StringComparer.OrdinalIgnoreCase);
		readonly Stack<SpriteRenderer> _availableRenderers = new Stack<SpriteRenderer>();
		readonly List<SpriteRenderer> _activeRenderers = new List<SpriteRenderer>();

		public IEnumerator Play(string projectileKey, Vector3 sourceWorldPosition, Vector3 targetWorldPosition)
		{
			if (string.IsNullOrWhiteSpace(projectileKey))
				yield break;

			Sprite sprite = null;
			yield return LoadSprite(projectileKey, loadedSprite => sprite = loadedSprite);
			if (sprite == null)
				yield break;

			ProjectileStyle style = GetStyle(projectileKey);
			Vector3 source = sourceWorldPosition;
			Vector3 target = targetWorldPosition + Vector3.up * TargetVerticalOffset;
			source.z = -0.1f;
			target.z = -0.1f;
			Vector3 direction = target - source;
			direction.z = 0f;
			float distance = direction.magnitude;
			if (distance <= 0.001f)
				yield break;

			float travelAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
			float duration = Mathf.Clamp(distance / style.UnitsPerSecond, MinimumFlightSeconds, MaximumFlightSeconds);
			SpriteRenderer renderer = RentRenderer();
			renderer.sprite = sprite;
			renderer.transform.position = source;
			renderer.transform.localScale = Vector3.one * style.Scale;
			renderer.gameObject.SetActive(true);

			float elapsed = 0f;
			while (elapsed < duration)
			{
				elapsed += Time.unscaledDeltaTime;
				float progress = Mathf.Clamp01(elapsed / duration);
				renderer.transform.position = Vector3.Lerp(source, target, progress);
				float spin = style.SpinDegrees == 0f ? 0f : -style.SpinDegrees * progress;
				renderer.transform.rotation = Quaternion.Euler(0f, 0f, travelAngle + spin);
				yield return null;
			}

			ReturnRenderer(renderer);
		}

		public void Clear()
		{
			for (int i = _activeRenderers.Count - 1; i >= 0; i--)
			{
				SpriteRenderer renderer = _activeRenderers[i];
				if (renderer == null)
					continue;

				renderer.gameObject.SetActive(false);
				_availableRenderers.Push(renderer);
			}

			_activeRenderers.Clear();
		}

		void OnDestroy()
		{
			Clear();
			foreach (AsyncOperationHandle<Sprite> handle in _handles.Values)
			{
				if (handle.IsValid())
					Addressables.Release(handle);
			}

			_handles.Clear();
			_sprites.Clear();
		}

		IEnumerator LoadSprite(string projectileKey, System.Action<Sprite> onLoaded)
		{
			if (_sprites.TryGetValue(projectileKey, out Sprite cachedSprite))
			{
				onLoaded?.Invoke(cachedSprite);
				yield break;
			}

			string address = $"projectile_{projectileKey}";
			AsyncOperationHandle<Sprite> handle = Addressables.LoadAssetAsync<Sprite>(address);
			while (handle.IsDone == false)
				yield return null;

			if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null)
			{
				_sprites[projectileKey] = handle.Result;
				_handles[projectileKey] = handle;
				onLoaded?.Invoke(handle.Result);
				yield break;
			}

			if (handle.IsValid())
				Addressables.Release(handle);

#if UNITY_EDITOR
			Sprite editorSprite = LoadEditorSprite(projectileKey);
			if (editorSprite != null)
			{
				_sprites[projectileKey] = editorSprite;
				onLoaded?.Invoke(editorSprite);
				yield break;
			}
#endif

			Debug.LogWarning($"Failed to load battle projectile. projectileKey={projectileKey}, address={address}");
		}

#if UNITY_EDITOR
		static Sprite LoadEditorSprite(string projectileKey)
		{
			string[] guids = AssetDatabase.FindAssets("t:Sprite", new[] { "Assets/@Resources/Art/Projectile" });
			foreach (string guid in guids)
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				string fileName = Path.GetFileNameWithoutExtension(path);
				if (string.Equals(fileName, projectileKey, System.StringComparison.OrdinalIgnoreCase))
					return AssetDatabase.LoadAssetAtPath<Sprite>(path);
			}

			return null;
		}
#endif

		SpriteRenderer RentRenderer()
		{
			SpriteRenderer renderer = _availableRenderers.Count > 0 ? _availableRenderers.Pop() : CreateRenderer();
			_activeRenderers.Add(renderer);
			return renderer;
		}

		SpriteRenderer CreateRenderer()
		{
			GameObject projectileObject = new GameObject("BattleProjectile", typeof(SpriteRenderer));
			projectileObject.transform.SetParent(transform, false);
			SpriteRenderer renderer = projectileObject.GetComponent<SpriteRenderer>();
			renderer.sortingOrder = ProjectileSortingOrder;
			projectileObject.SetActive(false);
			return renderer;
		}

		void ReturnRenderer(SpriteRenderer renderer)
		{
			if (renderer == null)
				return;

			_activeRenderers.Remove(renderer);
			renderer.gameObject.SetActive(false);
			_availableRenderers.Push(renderer);
		}

		static ProjectileStyle GetStyle(string projectileKey)
		{
			return Styles.TryGetValue(projectileKey, out ProjectileStyle style)
				? style
				: new ProjectileStyle(7f, 0.8f);
		}
	}
}
