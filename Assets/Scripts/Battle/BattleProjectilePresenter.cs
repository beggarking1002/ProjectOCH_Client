using System;
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
		const int ProjectileSortingOrder = 260;
		const float MinimumFlightSeconds = 0.14f;
		const float MaximumFlightSeconds = 0.56f;
		const float TargetVerticalOffset = 0.14f;
		const float HailSpawnHeight = 3.2f;
		const float HailFallSeconds = 0.42f;

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
			{ "hailstone", new ProjectileStyle(0f, 0.78f) },
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
			if (string.Equals(projectileKey, "hailstone", StringComparison.OrdinalIgnoreCase))
			{
				yield return PlayHailStone(sprite, style, targetWorldPosition);
				yield break;
			}

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

		// Hail is an area spell: it falls vertically onto the chosen tile instead of
		// travelling from the caster. The renderer is returned precisely at ground
		// contact, so it never remains embedded in the map.
		IEnumerator PlayHailStone(Sprite sprite, ProjectileStyle style, Vector3 targetWorldPosition)
		{
			Vector3 impact = targetWorldPosition + Vector3.up * TargetVerticalOffset;
			impact.z = -0.1f;
			Vector3 spawn = impact + Vector3.up * HailSpawnHeight;
			SpriteRenderer renderer = RentRenderer();
			renderer.sprite = sprite;
			renderer.transform.position = spawn;
			renderer.transform.rotation = Quaternion.identity;
			renderer.transform.localScale = Vector3.one * style.Scale;
			renderer.gameObject.SetActive(true);

			float elapsed = 0f;
			while (elapsed < HailFallSeconds)
			{
				elapsed += Time.unscaledDeltaTime;
				float progress = Mathf.Clamp01(elapsed / HailFallSeconds);
				// Quadratic easing gives the impact a clear falling acceleration.
				renderer.transform.position = Vector3.LerpUnclamped(spawn, impact, progress * progress);
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

	[DisallowMultipleComponent]
	public sealed class BattleSpriteEffectPresenter : MonoBehaviour
	{
		const int EffectSortingOrder = 260;

		readonly struct SpriteEffectStyle
		{
			public readonly int Columns;
			public readonly int Rows;
			public readonly float FrameSeconds;
			public readonly float Scale;

			public SpriteEffectStyle(int columns, int rows, float frameSeconds, float scale)
			{
				Columns = columns;
				Rows = rows;
				FrameSeconds = frameSeconds;
				Scale = scale;
			}
		}

		static readonly Dictionary<string, SpriteEffectStyle> Styles = new Dictionary<string, SpriteEffectStyle>(StringComparer.OrdinalIgnoreCase)
		{
			// Beige_Ice_Skill4_Effect.png is a 2208x1600, 4x4 animation sheet.
			{ "beige_ice_storm_center", new SpriteEffectStyle(4, 4, 0.055f, 0.36f) },
			{ "beige_ice_cold_hard_worker", new SpriteEffectStyle(4, 4, 0.055f, 0.36f) },
		};

		readonly Dictionary<string, Sprite[]> _framesByKey = new Dictionary<string, Sprite[]>(StringComparer.OrdinalIgnoreCase);
		readonly Dictionary<string, AsyncOperationHandle<Texture2D>> _textureHandles = new Dictionary<string, AsyncOperationHandle<Texture2D>>(StringComparer.OrdinalIgnoreCase);
		readonly Stack<SpriteRenderer> _availableRenderers = new Stack<SpriteRenderer>();
		readonly List<SpriteRenderer> _activeRenderers = new List<SpriteRenderer>();

		public IEnumerator Play(string effectKey, Vector3 worldPosition, Vector2? scaleOverride = null)
		{
			if (string.IsNullOrWhiteSpace(effectKey))
				yield break;

			Sprite[] frames = null;
			yield return LoadFrames(effectKey, loadedFrames => frames = loadedFrames);
			if (frames == null || frames.Length == 0)
				yield break;

			SpriteEffectStyle style = GetStyle(effectKey);
			SpriteRenderer renderer = RentRenderer();
			worldPosition.z = -0.12f;
			renderer.transform.position = worldPosition;
			renderer.transform.rotation = Quaternion.identity;
			Vector2 scale = scaleOverride ?? Vector2.one * style.Scale;
			renderer.transform.localScale = new Vector3(scale.x, scale.y, 1f);
			renderer.gameObject.SetActive(true);

			for (int i = 0; i < frames.Length; i++)
			{
				renderer.sprite = frames[i];
				yield return new WaitForSecondsRealtime(style.FrameSeconds);
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

				renderer.sprite = null;
				renderer.gameObject.SetActive(false);
				_availableRenderers.Push(renderer);
			}

			_activeRenderers.Clear();
		}

		void OnDestroy()
		{
			Clear();
			foreach (AsyncOperationHandle<Texture2D> handle in _textureHandles.Values)
			{
				if (handle.IsValid())
					Addressables.Release(handle);
			}

			_textureHandles.Clear();
			foreach (Sprite[] frames in _framesByKey.Values)
			{
				if (frames == null)
					continue;

				foreach (Sprite frame in frames)
				{
					if (frame != null)
						Destroy(frame);
				}
			}

			_framesByKey.Clear();
		}

		IEnumerator LoadFrames(string effectKey, Action<Sprite[]> onLoaded)
		{
			if (_framesByKey.TryGetValue(effectKey, out Sprite[] cachedFrames))
			{
				onLoaded?.Invoke(cachedFrames);
				yield break;
			}

			Texture2D texture = null;
			string address = GetAddress(effectKey);
			AsyncOperationHandle<Texture2D> handle = Addressables.LoadAssetAsync<Texture2D>(address);
			while (handle.IsDone == false)
				yield return null;

			if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null)
			{
				texture = handle.Result;
				_textureHandles[effectKey] = handle;
			}
			else
			{
				if (handle.IsValid())
					Addressables.Release(handle);

#if UNITY_EDITOR
				texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/@Resources/Art/SkillEffect/Beige_Ice_Skill4_Effect.png");
#endif
			}

			if (texture == null)
			{
				Debug.LogWarning($"Failed to load battle sprite effect. effectKey={effectKey}, address={address}");
				yield break;
			}

			Sprite[] frames = CreateFrames(texture, GetStyle(effectKey));
			_framesByKey[effectKey] = frames;
			onLoaded?.Invoke(frames);
		}

		static Sprite[] CreateFrames(Texture2D texture, SpriteEffectStyle style)
		{
			int frameWidth = texture.width / style.Columns;
			int frameHeight = texture.height / style.Rows;
			List<Sprite> frames = new List<Sprite>(style.Columns * style.Rows);
			for (int row = 0; row < style.Rows; row++)
			{
				for (int column = 0; column < style.Columns; column++)
				{
					Rect rect = new Rect(column * frameWidth, texture.height - ((row + 1) * frameHeight), frameWidth, frameHeight);
					frames.Add(Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), 100f));
				}
			}

			return frames.ToArray();
		}

		SpriteRenderer RentRenderer()
		{
			SpriteRenderer renderer = _availableRenderers.Count > 0 ? _availableRenderers.Pop() : CreateRenderer();
			_activeRenderers.Add(renderer);
			return renderer;
		}

		SpriteRenderer CreateRenderer()
		{
			GameObject effectObject = new GameObject("BattleSpriteEffect", typeof(SpriteRenderer));
			effectObject.transform.SetParent(transform, false);
			SpriteRenderer renderer = effectObject.GetComponent<SpriteRenderer>();
			renderer.sortingOrder = EffectSortingOrder;
			effectObject.SetActive(false);
			return renderer;
		}

		void ReturnRenderer(SpriteRenderer renderer)
		{
			if (renderer == null)
				return;

			_activeRenderers.Remove(renderer);
			renderer.sprite = null;
			renderer.gameObject.SetActive(false);
			_availableRenderers.Push(renderer);
		}

		static SpriteEffectStyle GetStyle(string effectKey)
		{
			return Styles.TryGetValue(effectKey, out SpriteEffectStyle style)
				? style
				: new SpriteEffectStyle(1, 1, 0.1f, 1f);
		}

		static string GetAddress(string effectKey)
		{
			// The ultimate deliberately reuses Storm Center's source animation at a
			// larger, one-shot radius rather than duplicating its texture asset.
			return string.Equals(effectKey, "beige_ice_cold_hard_worker", StringComparison.OrdinalIgnoreCase)
				? "effect_beige_ice_storm_center"
				: $"effect_{effectKey}";
		}
	}

	[DisallowMultipleComponent]
	public sealed class BattleFireTileEffectPresenter : MonoBehaviour
	{
		const string FireTileDamageEffectAddress = "Effect_FireTileDamage";
		GameObject _prefab;
		AsyncOperationHandle<GameObject> _prefabHandle;
		bool _isLoading;

		public void Preload()
		{
			if (_prefab != null || _isLoading)
				return;

			StartCoroutine(LoadPrefab());
		}

		public void Play(Vector3 worldPosition)
		{
			if (_prefab == null)
			{
				Preload();
				BattleFireTileDamageEffect.PlayFallback(transform, worldPosition);
				return;
			}

			GameObject instance = Instantiate(_prefab, worldPosition, Quaternion.identity, transform);
			BattleFireTileDamageEffect effect = instance.GetComponent<BattleFireTileDamageEffect>();
			if (effect != null)
				effect.Play();
			else
				Destroy(instance);
		}

		IEnumerator LoadPrefab()
		{
			_isLoading = true;
			_prefabHandle = Addressables.LoadAssetAsync<GameObject>(FireTileDamageEffectAddress);
			while (_prefabHandle.IsDone == false)
				yield return null;

			if (_prefabHandle.Status == AsyncOperationStatus.Succeeded)
				_prefab = _prefabHandle.Result;
			else if (_prefabHandle.IsValid())
			{
				Addressables.Release(_prefabHandle);
				Debug.LogWarning($"Failed to load fire-tile damage effect prefab. address={FireTileDamageEffectAddress}");
			}

			_isLoading = false;
		}

		void OnDestroy()
		{
			if (_prefabHandle.IsValid())
				Addressables.Release(_prefabHandle);
		}
	}
}
