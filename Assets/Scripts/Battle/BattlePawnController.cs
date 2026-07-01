using UnityEngine;

namespace Battle
{
	[DisallowMultipleComponent]
	public sealed class BattlePawnController : MonoBehaviour
	{
		const int DefaultSortingOrder = 20;
		const int TurnIndicatorSortingOrder = 40;

		BattleMapGrid _mapGrid;
		SpriteRenderer _spriteRenderer;
		GameObject _turnIndicator;

		public ulong PawnId { get; private set; }
		public bool IsMine { get; private set; }
		public AxialCoord Axial { get; private set; }
		public Protocol.BattlePawnInfo Info { get; private set; }

		void Awake()
		{
			_spriteRenderer = GetComponentInChildren<SpriteRenderer>();
		}

		public void Initialize(ulong pawnId, bool isMine, BattleMapGrid mapGrid, AxialCoord axial, Color tint, Protocol.BattlePawnInfo info = null)
		{
			PawnId = pawnId;
			IsMine = isMine;
			_mapGrid = mapGrid;
			Info = info?.Clone();

			if (_spriteRenderer == null)
				_spriteRenderer = GetComponentInChildren<SpriteRenderer>();

			if (_spriteRenderer != null)
			{
				_spriteRenderer.sortingOrder = DefaultSortingOrder;
				_spriteRenderer.color = tint;
			}

			EnsureTurnIndicator();
			SetTurnIndicatorVisible(false);
			SetAxial(axial);
		}

		public void SetAxial(AxialCoord axial)
		{
			Axial = axial;

			if (_mapGrid == null)
				return;

			transform.position = _mapGrid.AxialToWorldCenter(axial, transform.position.z);
		}

		public void SetTurnIndicatorVisible(bool visible)
		{
			EnsureTurnIndicator();
			_turnIndicator.SetActive(visible);
		}

		void EnsureTurnIndicator()
		{
			if (_turnIndicator != null)
				return;

			_turnIndicator = new GameObject("TurnIndicator");
			_turnIndicator.transform.SetParent(transform, false);
			_turnIndicator.transform.localPosition = new Vector3(0f, GetTurnIndicatorHeight(), 0f);

			TextMesh textMesh = _turnIndicator.AddComponent<TextMesh>();
			textMesh.text = "TURN";
			textMesh.anchor = TextAnchor.MiddleCenter;
			textMesh.alignment = TextAlignment.Center;
			textMesh.characterSize = 0.16f;
			textMesh.fontSize = 32;
			textMesh.color = Color.yellow;

			MeshRenderer renderer = _turnIndicator.GetComponent<MeshRenderer>();
			if (renderer != null)
				renderer.sortingOrder = TurnIndicatorSortingOrder;
		}

		float GetTurnIndicatorHeight()
		{
			if (_spriteRenderer == null)
				_spriteRenderer = GetComponentInChildren<SpriteRenderer>();

			if (_spriteRenderer == null)
				return 1.2f;

			return Mathf.Max(1.2f, _spriteRenderer.bounds.size.y * 0.65f + 0.35f);
		}
	}
}
