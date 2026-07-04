using UnityEngine;

namespace Battle
{
	[DisallowMultipleComponent]
	public sealed class BattlePawnController : MonoBehaviour
	{
		const int DefaultSortingOrder = 20;
		const int StatusBarBackgroundSortingOrder = 38;
		const int StatusBarFillSortingOrder = 39;
		const int TurnIndicatorSortingOrder = 40;
		const float StatusBarWidth = 0.72f;
		const float StatusBarHeight = 0.065f;
		const float StatusBarTrackPadding = 0.018f;
		const float StatusBarGap = 0.035f;

		static Sprite _statusBarSprite;
		BattleMapGrid _mapGrid;
		SpriteRenderer _spriteRenderer;
		GameObject _statusBars;
		SpriteRenderer _hpBarFill;
		SpriteRenderer _armorBarFill;
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
			EnsureStatusBars();
			SetTurnIndicatorVisible(false);
			RefreshStatusBars();
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
			RefreshStatusBars();
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
			RefreshStatusBars();
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
			RefreshStatusBars();
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

		void EnsureStatusBars()
		{
			if (_statusBars != null)
				return;

			_statusBars = new GameObject("PawnStatusBars");
			_statusBars.transform.SetParent(transform, false);
			_statusBars.transform.localPosition = new Vector3(0f, GetStatusBarsHeight(), 0f);

			float hpY = (StatusBarHeight + StatusBarGap) * 0.5f;
			float armorY = -hpY;
			Vector2 trackSize = new Vector2(StatusBarWidth + StatusBarTrackPadding * 2f, StatusBarHeight + StatusBarTrackPadding * 2f);
			Color trackColor = new Color(0.02f, 0.025f, 0.03f, 0.82f);

			CreateBarRenderer("HpBarTrack", _statusBars.transform, new Vector2(0f, hpY), trackSize, trackColor, StatusBarBackgroundSortingOrder);
			_hpBarFill = CreateBarRenderer("HpBarFill", _statusBars.transform, new Vector2(0f, hpY), new Vector2(StatusBarWidth, StatusBarHeight), new Color(0.82f, 0.18f, 0.16f, 1f), StatusBarFillSortingOrder);
			CreateBarRenderer("ArmorBarTrack", _statusBars.transform, new Vector2(0f, armorY), trackSize, trackColor, StatusBarBackgroundSortingOrder);
			_armorBarFill = CreateBarRenderer("ArmorBarFill", _statusBars.transform, new Vector2(0f, armorY), new Vector2(StatusBarWidth, StatusBarHeight), new Color(0.35f, 0.68f, 1f, 1f), StatusBarFillSortingOrder);
		}

		void RefreshStatusBars()
		{
			EnsureStatusBars();

			SetBarFill(_hpBarFill, GetRatio(Hp, MaxHp));
			SetBarFill(_armorBarFill, GetRatio(Armor, MaxArmor));
			_statusBars.transform.localPosition = new Vector3(0f, GetStatusBarsHeight(), 0f);

			if (_turnIndicator != null)
				_turnIndicator.transform.localPosition = new Vector3(0f, GetTurnIndicatorHeight(), 0f);
		}

		static SpriteRenderer CreateBarRenderer(string name, Transform parent, Vector2 localPosition, Vector2 size, Color color, int sortingOrder)
		{
			GameObject barObject = new GameObject(name);
			barObject.transform.SetParent(parent, false);
			barObject.transform.localPosition = new Vector3(localPosition.x, localPosition.y, 0f);
			barObject.transform.localScale = new Vector3(size.x, size.y, 1f);

			SpriteRenderer renderer = barObject.AddComponent<SpriteRenderer>();
			renderer.sprite = GetStatusBarSprite();
			renderer.color = color;
			renderer.sortingOrder = sortingOrder;
			return renderer;
		}

		static void SetBarFill(SpriteRenderer fill, float ratio)
		{
			if (fill == null)
				return;

			ratio = Mathf.Clamp01(ratio);
			fill.transform.localScale = new Vector3(StatusBarWidth * ratio, StatusBarHeight, 1f);
			fill.transform.localPosition = new Vector3((-StatusBarWidth + StatusBarWidth * ratio) * 0.5f, fill.transform.localPosition.y, 0f);
		}

		static float GetRatio(int value, int maxValue)
		{
			if (maxValue <= 0)
				return value > 0 ? 1f : 0f;

			return (float)value / maxValue;
		}

		static Sprite GetStatusBarSprite()
		{
			if (_statusBarSprite != null)
				return _statusBarSprite;

			Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
			texture.name = "Runtime_PawnStatusBarSprite";
			texture.SetPixel(0, 0, Color.white);
			texture.Apply();

			_statusBarSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
			return _statusBarSprite;
		}

		float GetTurnIndicatorHeight()
		{
			if (_spriteRenderer == null)
				_spriteRenderer = GetComponentInChildren<SpriteRenderer>();

			if (_spriteRenderer == null)
				return 1.55f;

			return Mathf.Max(1.55f, _spriteRenderer.bounds.size.y * 0.65f + 0.7f);
		}

		float GetStatusBarsHeight()
		{
			if (_spriteRenderer == null)
				_spriteRenderer = GetComponentInChildren<SpriteRenderer>();

			if (_spriteRenderer == null)
				return 1.2f;

			return Mathf.Max(1.2f, _spriteRenderer.bounds.size.y * 0.65f + 0.35f);
		}
	}
}
