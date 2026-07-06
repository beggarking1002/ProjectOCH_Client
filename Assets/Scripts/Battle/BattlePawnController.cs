using System;
using System.Collections;
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
		const float MoveSecondsPerTile = 0.28f;
		const float MinMoveDurationSeconds = 0.18f;
		const float MaxMoveDurationSeconds = 1.2f;
		const string VisualRootName = "visual";
		static readonly int IsMovingHash = Animator.StringToHash("isMoving");

		BattleMapGrid _mapGrid;
		Transform _visualRoot;
		SpriteRenderer _spriteRenderer;
		Animator _animator;
		PawnStatusWorldUI _statusWorldUi;
		PawnTeamRing _teamRing;
		GameObject _turnIndicator;
		Coroutine _moveCoroutine;

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
		public bool IsMoving { get; private set; }

		void Awake()
		{
			_visualRoot = FindVisualRoot();
			_spriteRenderer = FindVisualSpriteRenderer();
			_animator = FindVisualAnimator();
			_statusWorldUi = GetComponentInChildren<PawnStatusWorldUI>(true);
			_teamRing = GetComponentInChildren<PawnTeamRing>(true);
		}

		void OnDisable()
		{
			if (_moveCoroutine != null)
			{
				StopCoroutine(_moveCoroutine);
				_moveCoroutine = null;
			}

			SetMoving(false);
		}

		public void Initialize(ulong pawnId, bool isMine, BattleMapGrid mapGrid, AxialCoord axial, Color tint, Protocol.BattlePawnInfo info = null)
		{
			PawnId = pawnId;
			IsMine = isMine;
			_mapGrid = mapGrid;
			Info = info?.Clone();

			_visualRoot = FindVisualRoot();
			_spriteRenderer = FindVisualSpriteRenderer();
			_animator = FindVisualAnimator();

			if (_spriteRenderer != null)
			{
				_spriteRenderer.sortingOrder = DefaultSortingOrder;
				_spriteRenderer.color = tint;
			}

			EnsureTurnIndicator();
			EnsureTeamRing();
			EnsureStatusWorldUi();
			SetTurnIndicatorVisible(false);
			RefreshStatusWorldUi();
			SetAxial(axial);
			ApplyDeadVisualState();
		}

		public void SetAxial(AxialCoord axial)
		{
			if (_moveCoroutine != null)
			{
				StopCoroutine(_moveCoroutine);
				_moveCoroutine = null;
			}

			SetMoving(false);
			SetAxialState(axial);

			if (_mapGrid == null)
				return;

			transform.position = _mapGrid.AxialToWorldCenter(axial, transform.position.z);
		}

		public void MoveToAxial(AxialCoord axial, Action onComplete = null)
		{
			if (_moveCoroutine != null)
			{
				StopCoroutine(_moveCoroutine);
				_moveCoroutine = null;
			}

			if (_mapGrid == null || gameObject.activeInHierarchy == false)
			{
				SetAxial(axial);
				onComplete?.Invoke();
				return;
			}

			_moveCoroutine = StartCoroutine(MoveToAxialRoutine(axial, onComplete));
		}

		void SetAxialState(AxialCoord axial)
		{
			Axial = axial;
			if (Info != null)
				Info.Axial = new Protocol.AxialCoord { Q = axial.Q, R = axial.R };
		}

		IEnumerator MoveToAxialRoutine(AxialCoord targetAxial, Action onComplete)
		{
			AxialCoord startAxial = Axial;
			Vector3 start = transform.position;
			Vector3 target = _mapGrid.AxialToWorldCenter(targetAxial, transform.position.z);
			SetAxialState(targetAxial);

			float duration = GetMoveDuration(startAxial, targetAxial);
			if (duration <= 0f || Vector3.Distance(start, target) <= 0.001f)
			{
				transform.position = target;
				SetMoving(false);
				_moveCoroutine = null;
				onComplete?.Invoke();
				yield break;
			}

			SetMoving(true);
			float elapsed = 0f;
			while (elapsed < duration)
			{
				elapsed += Time.deltaTime;
				float t = Mathf.Clamp01(elapsed / duration);
				t = t * t * (3f - 2f * t);
				transform.position = Vector3.LerpUnclamped(start, target, t);
				yield return null;
			}

			transform.position = target;
			SetMoving(false);
			_moveCoroutine = null;
			onComplete?.Invoke();
		}

		static float GetMoveDuration(AxialCoord start, AxialCoord target)
		{
			int distance = GetAxialDistance(start, target);
			if (distance <= 0)
				return 0f;

			return Mathf.Clamp(distance * MoveSecondsPerTile, MinMoveDurationSeconds, MaxMoveDurationSeconds);
		}

		static int GetAxialDistance(AxialCoord a, AxialCoord b)
		{
			int dq = Mathf.Abs(a.Q - b.Q);
			int dr = Mathf.Abs(a.R - b.R);
			int ds = Mathf.Abs((-a.Q - a.R) - (-b.Q - b.R));
			return (dq + dr + ds) / 2;
		}

		void SetMoving(bool isMoving)
		{
			IsMoving = isMoving;

			if (_animator == null)
				_animator = FindVisualAnimator();

			if (_animator != null && HasBoolParameter(_animator, IsMovingHash))
				_animator.SetBool(IsMovingHash, isMoving);
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
				_spriteRenderer = FindVisualSpriteRenderer();

			if (_spriteRenderer != null)
				_spriteRenderer.enabled = isDead == false;

			if (_visualRoot != null)
				_visualRoot.gameObject.SetActive(isDead == false);

			if (_statusWorldUi != null)
				_statusWorldUi.gameObject.SetActive(isDead == false);

			if (_turnIndicator != null && isDead)
				_turnIndicator.SetActive(false);

			if (_teamRing != null)
				_teamRing.gameObject.SetActive(isDead == false);
		}

		void EnsureTeamRing()
		{
			if (_teamRing == null)
				_teamRing = GetComponentInChildren<PawnTeamRing>(true);

			if (_teamRing == null)
			{
				Debug.LogWarning($"{nameof(BattlePawnController)} requires a PawnTeamRing child prefab. pawnId={PawnId}, name={name}");
				return;
			}

			_teamRing.Initialize(IsMine);
			_teamRing.gameObject.SetActive(IsDead == false);
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
				Debug.LogWarning($"{nameof(BattlePawnController)} requires a PawnStatusWorldUI child prefab. pawnId={PawnId}, name={name}");
				return;
			}

			_statusWorldUi.transform.localPosition = new Vector3(0f, GetStatusWorldUiHeight(), 0f);
			_statusWorldUi.Initialize(IsMine);
		}

		void RefreshStatusWorldUi()
		{
			EnsureStatusWorldUi();
			if (_statusWorldUi == null)
				return;

			_statusWorldUi.SetValues(Hp, MaxHp, Armor, MaxArmor);
			_statusWorldUi.transform.localPosition = new Vector3(0f, GetStatusWorldUiHeight(), 0f);

			if (_turnIndicator != null)
				_turnIndicator.transform.localPosition = new Vector3(0f, GetTurnIndicatorHeight(), 0f);
		}

		float GetTurnIndicatorHeight()
		{
			if (_spriteRenderer == null)
				_spriteRenderer = FindVisualSpriteRenderer();

			if (_spriteRenderer == null)
				return 1.55f;

			return Mathf.Max(1.75f, GetLocalBoundsTop() + TurnIndicatorMargin);
		}

		float GetStatusWorldUiHeight()
		{
			if (_spriteRenderer == null)
				_spriteRenderer = FindVisualSpriteRenderer();

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

		Transform FindVisualRoot()
		{
			Transform direct = transform.Find(VisualRootName);
			if (direct != null)
				return direct;

			Transform[] children = GetComponentsInChildren<Transform>(true);
			for (int i = 0; i < children.Length; i++)
			{
				if (children[i] != transform && children[i].name == VisualRootName)
					return children[i];
			}

			return null;
		}

		SpriteRenderer FindVisualSpriteRenderer()
		{
			if (_visualRoot == null)
				_visualRoot = FindVisualRoot();

			if (_visualRoot != null)
			{
				SpriteRenderer visualRenderer = _visualRoot.GetComponentInChildren<SpriteRenderer>(true);
				if (visualRenderer != null)
					return visualRenderer;
			}

			SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
			for (int i = 0; i < renderers.Length; i++)
			{
				SpriteRenderer renderer = renderers[i];
				if (renderer == null)
					continue;

				if (renderer.GetComponentInParent<PawnStatusWorldUI>() != null)
					continue;

				return renderer;
			}

			return null;
		}

		Animator FindVisualAnimator()
		{
			if (_visualRoot == null)
				_visualRoot = FindVisualRoot();

			if (_visualRoot != null)
			{
				Animator visualAnimator = _visualRoot.GetComponentInChildren<Animator>(true);
				if (visualAnimator != null)
					return visualAnimator;
			}

			return GetComponentInChildren<Animator>(true);
		}

		static bool HasBoolParameter(Animator animator, int nameHash)
		{
			if (animator == null || animator.runtimeAnimatorController == null)
				return false;

			AnimatorControllerParameter[] parameters = animator.parameters;
			for (int i = 0; i < parameters.Length; i++)
			{
				AnimatorControllerParameter parameter = parameters[i];
				if (parameter.type == AnimatorControllerParameterType.Bool && parameter.nameHash == nameHash)
					return true;
			}

			return false;
		}
	}
}
