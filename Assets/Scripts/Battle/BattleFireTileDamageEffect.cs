using System.Collections;
using UnityEngine;

namespace Battle
{
	[DisallowMultipleComponent]
	public sealed class BattleFireTileDamageEffect : MonoBehaviour
	{
		static Sprite _flameSprite;

		[SerializeField] int sortingOrder = 30;
		[SerializeField] float durationSeconds = 0.42f;
		[SerializeField] float outerRadius = 0.28f;
		[SerializeField] float innerRadius = 0.14f;
		[SerializeField] Color outerFlameColor = new Color(1f, 0.24f, 0.04f, 0.9f);
		[SerializeField] Color innerFlameColor = new Color(1f, 0.78f, 0.12f, 0.95f);

		public void Play()
		{
			StartCoroutine(PlayRoutine());
		}

		public static void PlayFallback(Transform parent, Vector3 worldPosition)
		{
			GameObject fallback = new GameObject("FireTileDamageEffect_Fallback");
			fallback.transform.SetParent(parent, false);
			fallback.transform.position = worldPosition;
			fallback.AddComponent<BattleFireTileDamageEffect>().Play();
		}

		IEnumerator PlayRoutine()
		{
			transform.position += Vector3.up * 0.1f;
			SpriteRenderer[] flames = GetComponentsInChildren<SpriteRenderer>(true);
			if (flames.Length == 0)
				flames = CreateFallbackFlames();

			Vector3[] starts = new Vector3[flames.Length];
			Color[] colors = new Color[flames.Length];
			for (int i = 0; i < flames.Length; i++)
			{
				SpriteRenderer flame = flames[i];
				starts[i] = flame.transform.localPosition;
				colors[i] = flame.color;
				flame.sortingOrder = sortingOrder;
				flame.gameObject.SetActive(true);
			}

			float elapsed = 0f;
			while (elapsed < durationSeconds)
			{
				elapsed += Time.unscaledDeltaTime;
				float ratio = Mathf.Clamp01(elapsed / durationSeconds);
				for (int i = 0; i < flames.Length; i++)
				{
					SpriteRenderer flame = flames[i];
					if (flame == null)
						continue;

					flame.transform.localPosition = starts[i] + Vector3.up * (0.45f * ratio);
					flame.transform.localScale = Vector3.Lerp(new Vector3(0.22f, 0.34f, 1f), new Vector3(0.05f, 0.52f, 1f), ratio);
					Color color = colors[i];
					color.a *= 1f - ratio;
					flame.color = color;
				}

				yield return null;
			}

			Destroy(gameObject);
		}

		SpriteRenderer[] CreateFallbackFlames()
		{
			const int flameCount = 6;
			SpriteRenderer[] flames = new SpriteRenderer[flameCount];
			for (int i = 0; i < flameCount; i++)
			{
				float angle = i * Mathf.PI * 2f / flameCount;
				float radius = i % 2 == 0 ? outerRadius : innerRadius;
				GameObject flameObject = new GameObject($"FireBurst_{i}", typeof(SpriteRenderer));
				flameObject.transform.SetParent(transform, false);
				flameObject.transform.localPosition = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius * 0.45f, 0f);
				flameObject.transform.localRotation = Quaternion.Euler(0f, 0f, i * 360f / flameCount + 45f);
				flameObject.transform.localScale = new Vector3(0.22f, 0.34f, 1f);
				SpriteRenderer renderer = flameObject.GetComponent<SpriteRenderer>();
				renderer.sprite = GetFlameSprite();
				renderer.color = i % 2 == 0 ? outerFlameColor : innerFlameColor;
				flames[i] = renderer;
			}

			return flames;
		}

		static Sprite GetFlameSprite()
		{
			if (_flameSprite != null)
				return _flameSprite;

			Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
			texture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
			texture.Apply(false, true);
			texture.hideFlags = HideFlags.HideAndDontSave;
			_flameSprite = Sprite.Create(texture, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f), 2f);
			_flameSprite.hideFlags = HideFlags.HideAndDontSave;
			return _flameSprite;
		}
	}
}
