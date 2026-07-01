using UnityEngine;

namespace Battle
{
	[DisallowMultipleComponent]
	public sealed class BattlePawnController : MonoBehaviour
	{
		const int DefaultSortingOrder = 20;

		BattleMapGrid _mapGrid;
		SpriteRenderer _spriteRenderer;

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

			SetAxial(axial);
		}

		public void SetAxial(AxialCoord axial)
		{
			Axial = axial;

			if (_mapGrid == null)
				return;

			transform.position = _mapGrid.AxialToWorldCenter(axial, transform.position.z);
		}
	}
}
