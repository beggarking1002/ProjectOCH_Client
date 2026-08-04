using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Battle
{
	[DisallowMultipleComponent]
	public sealed class BeigeIce : Beige
	{
		const string StormCenterAuraSkillKey = "BEIGE_ICE_STORM_CENTER";
		const string ColdHardWorkerEmpoweredStatusKey = "BEIGE_ICE_COLD_HARD_WORKER_EMPOWERED";

		BeigeIceStormCenterAuraVisual _stormCenterAuraVisual;

		protected override void OnPawnInitialized()
		{
			RefreshStormCenterAura();
		}

		protected override void OnPawnStateChanged()
		{
			RefreshStormCenterAura();
		}

		protected override void OnAxialChanged()
		{
			RefreshStormCenterAura();
		}

		protected override void OnPawnDisabled()
		{
			if (_stormCenterAuraVisual != null)
				_stormCenterAuraVisual.Hide();
		}

		void RefreshStormCenterAura()
		{
			if (Auras.TryGetValue(StormCenterAuraSkillKey, out AuraState aura)
				&& IsDead == false
				&& MapGrid != null)
			{
				if (_stormCenterAuraVisual == null)
					_stormCenterAuraVisual = GetComponent<BeigeIceStormCenterAuraVisual>()
						?? gameObject.AddComponent<BeigeIceStormCenterAuraVisual>();

				_stormCenterAuraVisual.Show(
					MapGrid,
					Axial,
					aura.Radius,
					Statuses.ContainsKey(ColdHardWorkerEmpoweredStatusKey));
				return;
			}

			if (_stormCenterAuraVisual != null)
				_stormCenterAuraVisual.Hide();
		}
	}

	// Presentation-only Beige Ice effect. Damage and range judgement remain server-authoritative.
	[DisallowMultipleComponent]
	sealed class BeigeIceStormCenterAuraVisual : MonoBehaviour
	{
		const int SortingOrder = 19;
		const int SpriteEffectSortingOrder = 43;
		const float PulseSpeed = 2.4f;
		const float MinAlpha = 0.38f;
		const float MaxAlpha = 0.78f;
		const float SpriteFrameSeconds = 0.055f;
		const float SpriteEffectRangeScale = 1.2f;
		const string SpriteEffectAddress = "effect_beige_ice_storm_center";
		const string SpriteEffectEditorPath = "Assets/@Resources/Art/SkillEffect/Beige_Ice_Skill4_Effect.png";

		LineRenderer _ring;
		Material _material;
		GameObject _spriteEffectObject;
		SpriteRenderer _spriteEffectRenderer;
		GameObject _ultimateEffectObject;
		SpriteRenderer _ultimateEffectRenderer;
		Coroutine _spriteEffectCoroutine;
		AsyncOperationHandle<Texture2D> _spriteEffectTextureHandle;
		bool _hasSpriteEffectTextureHandle;
		Sprite[] _spriteEffectFrames;
		BattleMapGrid _mapGrid;
		AxialCoord _axial;
		int _radius;
		bool _visible;
		bool _showUltimateEffect;

		public void Show(BattleMapGrid mapGrid, AxialCoord axial, int radius, bool showUltimateEffect)
		{
			if (mapGrid == null || radius <= 0)
			{
				Hide();
				return;
			}

			_mapGrid = mapGrid;
			_axial = axial;
			_radius = radius;
			_showUltimateEffect = showUltimateEffect;
			EnsureRing();
			UpdateRingPositions();
			_visible = true;
			_ring.enabled = true;
			ShowSpriteEffect();
		}

		public void Hide()
		{
			_visible = false;
			_showUltimateEffect = false;
			if (_ring != null)
				_ring.enabled = false;
			HideSpriteEffect();
		}

		void LateUpdate()
		{
			if (_visible == false || _ring == null || _ring.enabled == false)
				return;

			UpdateRingPositions();
			float pulse = (Mathf.Sin(Time.time * PulseSpeed) + 1f) * 0.5f;
			Color color = new Color(0.36f, 0.86f, 1f, Mathf.Lerp(MinAlpha, MaxAlpha, pulse));
			_ring.startColor = color;
			_ring.endColor = color;
		}

		void OnDestroy()
		{
			HideSpriteEffect();
			if (_hasSpriteEffectTextureHandle && _spriteEffectTextureHandle.IsValid())
				Addressables.Release(_spriteEffectTextureHandle);

			if (_spriteEffectFrames != null)
			{
				foreach (Sprite frame in _spriteEffectFrames)
				{
					if (frame != null)
						Destroy(frame);
				}
			}

			if (_material != null)
				Destroy(_material);
		}

		void ShowSpriteEffect()
		{
			EnsureSpriteEffectRenderer();
			if (_ultimateEffectRenderer != null)
				_ultimateEffectRenderer.enabled = _showUltimateEffect && _spriteEffectFrames != null;

			if (_spriteEffectCoroutine == null)
				_spriteEffectCoroutine = StartCoroutine(PlaySpriteEffectLoop());
		}

		void HideSpriteEffect()
		{
			if (_spriteEffectCoroutine != null)
			{
				StopCoroutine(_spriteEffectCoroutine);
				_spriteEffectCoroutine = null;
			}

			if (_spriteEffectRenderer != null)
			{
				_spriteEffectRenderer.sprite = null;
				_spriteEffectRenderer.enabled = false;
			}

			if (_ultimateEffectRenderer != null)
			{
				_ultimateEffectRenderer.sprite = null;
				_ultimateEffectRenderer.enabled = false;
			}
		}

		void EnsureSpriteEffectRenderer()
		{
			if (_spriteEffectRenderer == null)
			{
				_spriteEffectObject = CreateSpriteEffectObject("StormCenterSpriteEffect", SpriteEffectSortingOrder, -0.12f, out _spriteEffectRenderer);
			}

			if (_ultimateEffectRenderer == null)
			{
				// This sits beneath the normal aura loop, so both remain readable together.
				_ultimateEffectObject = CreateSpriteEffectObject("ColdHardWorkerSpriteEffect", SpriteEffectSortingOrder - 1, -0.13f, out _ultimateEffectRenderer);
			}
		}

		GameObject CreateSpriteEffectObject(string name, int sortingOrder, float localZ, out SpriteRenderer renderer)
		{
			GameObject effectObject = new GameObject(name, typeof(SpriteRenderer));
			effectObject.transform.SetParent(transform, false);
			effectObject.transform.localPosition = new Vector3(0f, 0f, localZ);
			effectObject.transform.localScale = Vector3.one;
			renderer = effectObject.GetComponent<SpriteRenderer>();
			renderer.sortingOrder = sortingOrder;
			renderer.enabled = false;
			return effectObject;
		}

		IEnumerator PlaySpriteEffectLoop()
		{
			if (_spriteEffectFrames == null)
				yield return LoadSpriteEffectFrames();

			if (_spriteEffectFrames == null || _spriteEffectFrames.Length == 0 || _spriteEffectRenderer == null)
			{
				_spriteEffectCoroutine = null;
				yield break;
			}

			_spriteEffectRenderer.enabled = true;
			_ultimateEffectRenderer.enabled = _showUltimateEffect;
			while (_visible)
			{
				for (int i = 0; i < _spriteEffectFrames.Length && _visible; i++)
				{
					_spriteEffectRenderer.sprite = _spriteEffectFrames[i];
					_ultimateEffectRenderer.enabled = _showUltimateEffect;
					if (_showUltimateEffect)
						_ultimateEffectRenderer.sprite = _spriteEffectFrames[i];
					yield return new WaitForSecondsRealtime(SpriteFrameSeconds);
				}
			}

			_spriteEffectCoroutine = null;
		}

		IEnumerator LoadSpriteEffectFrames()
		{
			AsyncOperationHandle<Texture2D> handle = Addressables.LoadAssetAsync<Texture2D>(SpriteEffectAddress);
			while (handle.IsDone == false)
				yield return null;

			Texture2D texture = null;
			if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null)
			{
				texture = handle.Result;
				_spriteEffectTextureHandle = handle;
				_hasSpriteEffectTextureHandle = true;
			}
			else
			{
				if (handle.IsValid())
					Addressables.Release(handle);

#if UNITY_EDITOR
				texture = AssetDatabase.LoadAssetAtPath<Texture2D>(SpriteEffectEditorPath);
#endif
			}

			if (texture == null)
			{
				Debug.LogWarning($"Failed to load Storm Center sprite effect. address={SpriteEffectAddress}");
				yield break;
			}

			_spriteEffectFrames = CreateSpriteEffectFrames(texture);
			UpdateSpriteEffectScale();
		}

		static Sprite[] CreateSpriteEffectFrames(Texture2D texture)
		{
			const int columns = 4;
			const int rows = 4;
			int frameWidth = texture.width / columns;
			int frameHeight = texture.height / rows;
			List<Sprite> frames = new List<Sprite>(columns * rows);
			for (int row = 0; row < rows; row++)
			{
				for (int column = 0; column < columns; column++)
				{
					Rect rect = new Rect(column * frameWidth, texture.height - ((row + 1) * frameHeight), frameWidth, frameHeight);
					frames.Add(Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), 100f));
				}
			}

			return frames.ToArray();
		}

		void EnsureRing()
		{
			if (_ring != null)
				return;

			_ring = gameObject.AddComponent<LineRenderer>();
			_ring.useWorldSpace = true;
			_ring.loop = true;
			_ring.positionCount = 6;
			_ring.startWidth = 0.055f;
			_ring.endWidth = 0.055f;
			_ring.numCornerVertices = 2;
			_ring.numCapVertices = 2;
			_ring.sortingOrder = SortingOrder;
			Shader shader = Shader.Find("Sprites/Default");
			if (shader != null)
			{
				_material = new Material(shader);
				_ring.material = _material;
			}
		}

		void UpdateRingPositions()
		{
			if (_ring == null || _mapGrid == null)
				return;

			Vector3 center = _mapGrid.AxialToWorldCenter(_axial, transform.position.z);
			List<Vector3> perimeter = BuildPerimeter(_radius);

			perimeter.Sort((left, right) =>
				Mathf.Atan2(left.y - center.y, left.x - center.x)
					.CompareTo(Mathf.Atan2(right.y - center.y, right.x - center.x)));

			for (int index = 0; index < perimeter.Count; index++)
				_ring.SetPosition(index, perimeter[index]);

			UpdateSpriteEffectScale();
		}

		void UpdateSpriteEffectScale()
		{
			if (_spriteEffectObject == null || _spriteEffectFrames == null || _spriteEffectFrames.Length == 0 || _mapGrid == null)
				return;

			// The server increases the aura's gameplay radius to two while the ultimate
			// is active. Keep the original storm loop visibly at one tile, then layer
			// the separate two-tile loop beneath it so the two effects do not collapse
			// into a single same-sized animation.
			int baseEffectRadius = _showUltimateEffect ? 1 : _radius;
			SetSpriteEffectScale(_spriteEffectObject, BuildPerimeter(baseEffectRadius));
			if (_showUltimateEffect && _ultimateEffectObject != null)
				SetSpriteEffectScale(_ultimateEffectObject, BuildPerimeter(2));
		}

		List<Vector3> BuildPerimeter(int radius)
		{
			List<Vector3> perimeter = new List<Vector3>(6);
			for (int direction = 0; direction < 6; direction++)
			{
				AxialCoord edge = _axial;
				for (int step = 0; step < radius; step++)
					edge = _mapGrid.GetNeighbor(edge, direction);

				perimeter.Add(_mapGrid.AxialToWorldCenter(edge, transform.position.z));
			}

			return perimeter;
		}

		void SetSpriteEffectScale(GameObject effectObject, List<Vector3> perimeter)
		{
			if (effectObject == null)
				return;

			float minX = float.MaxValue;
			float maxX = float.MinValue;
			float minY = float.MaxValue;
			float maxY = float.MinValue;
			foreach (Vector3 point in perimeter)
			{
				minX = Mathf.Min(minX, point.x);
				maxX = Mathf.Max(maxX, point.x);
				minY = Mathf.Min(minY, point.y);
				maxY = Mathf.Max(maxY, point.y);
			}

			Sprite firstFrame = _spriteEffectFrames[0];
			float sourceWidth = firstFrame.rect.width / firstFrame.pixelsPerUnit;
			float sourceHeight = firstFrame.rect.height / firstFrame.pixelsPerUnit;
			if (sourceWidth <= 0f || sourceHeight <= 0f)
				return;

			// The blue aura line joins these same perimeter points. The source art has
			// transparent margins, so give it a small overhang beyond that box to make
			// the storm read as covering the whole affected area at larger radii.
			effectObject.transform.localScale = new Vector3(
				((maxX - minX) / sourceWidth) * SpriteEffectRangeScale,
				((maxY - minY) / sourceHeight) * SpriteEffectRangeScale,
				1f);
		}
	}
}
