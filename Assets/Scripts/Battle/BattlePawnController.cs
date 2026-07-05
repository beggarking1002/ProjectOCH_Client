using UnityEngine;

namespace Battle
{
	[DisallowMultipleComponent]
	public sealed class BattlePawnController : MonoBehaviour
	{
		const int DefaultSortingOrder = 20;
		const int TurnIndicatorSortingOrder = 41;
		const float StatusWorldUiMargin = 0.18f;
		const float TurnIndicatorMargin = 0.52f;

		BattleMapGrid _mapGrid;
		SpriteRenderer _spriteRenderer;
		PawnStatusWorldUI _statusWorldUi;
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
		public bool IsDead => Info != null && Info.IsDead;

		void Awake()
		{
			_spriteRenderer = GetComponentInChildren<SpriteRenderer>();
			_statusWorldUi = GetComponentInChildren<PawnStatusWorldUI>(true);
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
			EnsureStatusWorldUi();
			SetTurnIndicatorVisible(false);
			RefreshStatusWorldUi();
			SetAxial(axial);
			ApplyDeadVisualState();
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
			RefreshStatusWorldUi();
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
			Info.IsDead = delta.IsDead;
			RefreshStatusWorldUi();
			ApplyDeadVisualState();
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
			RefreshStatusWorldUi();
		}

		public void ApplyDead(ulong killerPawnId)
		{
			EnsureInfo();
			Info.Hp = 0;
			Info.CurrentAp = 0;
			Info.CanMove = false;
			Info.IsDead = true;
			SetTurnIndicatorVisible(false);
			RefreshStatusWorldUi();
			ApplyDeadVisualState();
			Debug.Log($"Battle pawn dead. pawnId={PawnId}, killerPawnId={killerPawnId}");
		}

		public void SetTurnIndicatorVisible(bool visible)
		{
			EnsureTurnIndicator();
			_turnIndicator.SetActive(visible && IsDead == false);
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

		void ApplyDeadVisualState()
		{
			bool isDead = IsDead;

			if (_spriteRenderer == null)
				_spriteRenderer = GetComponentInChildren<SpriteRenderer>();

			if (_spriteRenderer != null)
				_spriteRenderer.enabled = isDead == false;

			if (_statusWorldUi != null)
				_statusWorldUi.gameObject.SetActive(isDead == false);

			if (_turnIndicator != null && isDead)
				_turnIndicator.SetActive(false);
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

		void EnsureStatusWorldUi()
		{
			if (_statusWorldUi == null)
				_statusWorldUi = GetComponentInChildren<PawnStatusWorldUI>(true);

			if (_statusWorldUi == null)
			{
				GameObject statusObject = new GameObject("PawnStatusWorldUI_RuntimeFallback");
				statusObject.transform.SetParent(transform, false);
				_statusWorldUi = statusObject.AddComponent<PawnStatusWorldUI>();
			}

			_statusWorldUi.transform.localPosition = new Vector3(0f, GetStatusWorldUiHeight(), 0f);
			_statusWorldUi.Initialize(IsMine);
		}

		void RefreshStatusWorldUi()
		{
			EnsureStatusWorldUi();
			_statusWorldUi.SetValues(Hp, MaxHp, Armor, MaxArmor);
			_statusWorldUi.transform.localPosition = new Vector3(0f, GetStatusWorldUiHeight(), 0f);

			if (_turnIndicator != null)
				_turnIndicator.transform.localPosition = new Vector3(0f, GetTurnIndicatorHeight(), 0f);
		}

		float GetTurnIndicatorHeight()
		{
			if (_spriteRenderer == null)
				_spriteRenderer = GetComponentInChildren<SpriteRenderer>();

			if (_spriteRenderer == null)
				return 1.55f;

			return Mathf.Max(1.75f, GetLocalBoundsTop() + TurnIndicatorMargin);
		}

		float GetStatusWorldUiHeight()
		{
			if (_spriteRenderer == null)
				_spriteRenderer = GetComponentInChildren<SpriteRenderer>();

			if (_spriteRenderer == null)
				return 1.2f;

			return Mathf.Max(1.2f, GetLocalBoundsTop() + StatusWorldUiMargin);
		}

		float GetLocalBoundsTop()
		{
			Bounds bounds = _spriteRenderer.bounds;
			Vector3 localTop = transform.InverseTransformPoint(new Vector3(bounds.center.x, bounds.max.y, bounds.center.z));
			return localTop.y;
		}
	}
}
