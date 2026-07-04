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
		public int Hp => Info != null ? Info.Hp : 0;
		public int MaxHp => Info != null ? Info.MaxHp : 0;
		public int Armor => Info != null ? Info.Armor : 0;
		public int MaxArmor => Info != null ? Info.MaxArmor : 0;
		public int CurrentAp => Info != null ? Info.CurrentAp : 2;
		public int MoveRange => Info != null ? Info.MoveRange : 0;
		public bool CanMove => Info == null || Info.CanMove;
		public bool UsedSubActionThisTurn => Info != null && Info.UsedSubActionThisTurn;
		public bool UsedUltimate => Info != null && Info.UsedUltimate;
		public bool IsShieldUnit => Info != null && Info.IsShieldUnit;
		public bool IsMelee => Info == null || Info.IsMelee;

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
			if (Info != null)
				Info.Axial = new Protocol.AxialCoord { Q = axial.Q, R = axial.R };

			if (_mapGrid == null)
				return;

			transform.position = _mapGrid.AxialToWorldCenter(axial, transform.position.z);
		}

		public void ApplyHp(int hp)
		{
			EnsureInfo();
			Info.Hp = hp;
		}

		public void ApplyDelta(Protocol.BattlePawnDelta delta)
		{
			if (delta == null || delta.PawnId != PawnId)
				return;

			EnsureInfo();
			Info.Hp = delta.Hp;
			Info.Armor = delta.Armor;
			Info.CurrentAp = delta.CurrentAp;
			Info.CanMove = delta.CanMove;
			Info.UsedSubActionThisTurn = delta.UsedSubActionThisTurn;
			Info.UsedUltimate = delta.UsedUltimate;
		}

		public void ApplyTurnState(int currentAp, bool canMove, bool usedSubActionThisTurn, bool usedUltimate)
		{
			EnsureInfo();
			Info.CurrentAp = currentAp;
			Info.CanMove = canMove;
			Info.UsedSubActionThisTurn = usedSubActionThisTurn;
			Info.UsedUltimate = usedUltimate;
		}

		public void ApplyTurnState(int currentAp, bool canMove)
		{
			EnsureInfo();
			Info.CurrentAp = currentAp;
			Info.CanMove = canMove;
		}

		public void ApplyArmor(int armor)
		{
			EnsureInfo();
			Info.Armor = armor;
		}

		public void SetTurnIndicatorVisible(bool visible)
		{
			EnsureTurnIndicator();
			_turnIndicator.SetActive(visible);
		}

		void EnsureInfo()
		{
			if (Info != null)
				return;

			Info = new Protocol.BattlePawnInfo
			{
				PawnId = PawnId,
				Axial = new Protocol.AxialCoord { Q = Axial.Q, R = Axial.R },
				Hp = 0,
				MaxHp = 0,
				CurrentAp = 2,
				CanMove = true,
				IsMelee = true,
			};
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
