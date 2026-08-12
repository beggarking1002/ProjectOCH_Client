using UnityEngine;

namespace Battle
{
	[DisallowMultipleComponent]
	public sealed class PawnTeamRing : MonoBehaviour
	{
		const int SegmentCount = 72;
		const float RadiusX = 0.46f;
		const float RadiusY = 0.2f;
		const float LineWidth = 0.055f;
		// Ring band: above tile/range indicators (10), below every pawn sprite (160+).
		const int SortingOrder = 100;

		public static readonly Color AllyColor = new Color(0.15f, 0.52f, 1f, 0.92f);
		static readonly Color EnemyColor = new Color(1f, 0.16f, 0.13f, 0.92f);

		LineRenderer _lineRenderer;
		Material _material;

		void Awake()
		{
			EnsureRenderer();
		}

		public void Initialize(bool isMine)
		{
			if (gameObject.activeSelf == false)
				gameObject.SetActive(true);

			EnsureRenderer();
			SetColor(isMine ? AllyColor : EnemyColor);
		}

		void EnsureRenderer()
		{
			if (_lineRenderer == null)
				_lineRenderer = GetComponent<LineRenderer>();

			if (_lineRenderer == null)
				_lineRenderer = gameObject.AddComponent<LineRenderer>();

			_lineRenderer.useWorldSpace = false;
			_lineRenderer.loop = true;
			_lineRenderer.positionCount = SegmentCount;
			_lineRenderer.widthMultiplier = LineWidth;
			_lineRenderer.numCornerVertices = 4;
			_lineRenderer.numCapVertices = 4;
			_lineRenderer.sortingOrder = SortingOrder;

			if (_lineRenderer.sharedMaterial == null)
			{
				if (_material == null)
					_material = new Material(Shader.Find("Sprites/Default"));

				_lineRenderer.sharedMaterial = _material;
			}

			for (int i = 0; i < SegmentCount; i++)
			{
				float angle = Mathf.PI * 2f * i / SegmentCount;
				_lineRenderer.SetPosition(i, new Vector3(Mathf.Cos(angle) * RadiusX, Mathf.Sin(angle) * RadiusY, 0f));
			}
		}

		void SetColor(Color color)
		{
			if (_lineRenderer == null)
				return;

			_lineRenderer.startColor = color;
			_lineRenderer.endColor = color;
		}
	}
}
