using System;
using System.Collections;
using System.Collections.Generic;
using App;
using UnityEngine;

namespace Battle
{
	[DisallowMultipleComponent]
	public class BattlePawn : MonoBehaviour
	{
		const int DefaultSortingOrder = 20;
		const int TurnIndicatorSortingOrder = 41;
		const float StatusWorldUiMargin = 0.18f;
		const float TurnIndicatorMargin = 0.52f;
		const float MoveSecondsPerTile = 0.28f;
		const float MinMoveDurationSeconds = 0.18f;
		const float MaxMoveDurationSeconds = 1.2f;
		// These complete before the 0.5 s combat-log beat finishes, so an evading
		// pawn can immediately answer with a counter-lunge on the next beat.
		const float EvadePresentationDurationSeconds = 0.44f;
		const float EvadePresentationDistance = 0.20f;
		const float EvadePresentationJumpHeight = 0.075f;
		const float MeleePresentationDurationSeconds = 0.42f;
		const float MeleePresentationDistance = 0.18f;
		const float MeleePresentationHopHeight = 0.035f;
		const string VisualRootName = "visual";
		const string ProjectileOriginName = "ProjectileOrigin";
		const float FallbackProjectileOriginHeight = 0.55f;
		static readonly int IsMovingHash = Animator.StringToHash("isMoving");
		const string DefaultSkillTrigger = "Skill1";

		BattleMapGrid _mapGrid;
		Transform _visualRoot;
		SpriteRenderer _spriteRenderer;
		Animator _animator;
		Transform _projectileOrigin;
		PawnStatusWorldUI _statusWorldUi;
		PawnTeamRing _teamRing;
		GameObject _turnIndicator;
		Coroutine _moveCoroutine;
		Coroutine _combatPresentationCoroutine;
		Vector3 _combatPresentationBaseLocalPosition;
		readonly Dictionary<Protocol.BattleResourceType, ResourceState> _resources = new Dictionary<Protocol.BattleResourceType, ResourceState>();
		readonly Dictionary<ulong, BarrierState> _barriers = new Dictionary<ulong, BarrierState>();
		readonly Dictionary<string, StatusState> _statuses = new Dictionary<string, StatusState>(StringComparer.Ordinal);
		readonly Dictionary<string, AuraState> _auras = new Dictionary<string, AuraState>(StringComparer.Ordinal);

		public ulong PawnId { get; private set; }
		public bool IsMine { get; private set; }
		public AxialCoord Axial { get; private set; }
		public Protocol.BattlePawnInfo Info { get; private set; }
		public int Hp => Info != null ? Info.Hp : 0;
		public int MaxHp => Info != null ? Info.MaxHp : 0;
		public int Armor => Info != null ? Info.Armor : 0;
		public int MaxArmor => Info != null ? Info.MaxArmor : 0;
		// Shield is the server-authoritative UI aggregate: armor + every temporary barrier.
		public int ShieldCurrent => Info != null ? Info.ShieldCurrent : 0;
		public int ShieldMax => Info != null ? Info.ShieldMax : 0;
		public int MoveRange => Info != null ? Info.MoveRange : 0;
		public bool CanMove => Info == null || Info.CanMove;
		public bool UsedNormalSkillThisTurn => Info != null && Info.UsedNormalSkillThisTurn;
		public bool UsedSubActionThisTurn => Info != null && Info.UsedSubActionThisTurn;
		public bool UsedUltimate => Info != null && Info.UsedUltimate;
		public bool IsActionBlocked => Info != null && Info.IsActionBlocked;
		public int ZocReactionsUsedThisTurn => Info != null ? Info.ZocReactionsUsedThisTurn : 0;
		public Protocol.BattlePawnRole Role => Info != null ? Info.Role : Protocol.BattlePawnRole.None;
		public bool IsShieldUnit => Role == Protocol.BattlePawnRole.Tanker;
		public bool IsMelee => Role == Protocol.BattlePawnRole.Tanker
			|| Role == Protocol.BattlePawnRole.Melee
			|| Role == Protocol.BattlePawnRole.Spear;
		public bool IsDead => Info != null && Info.IsDead;
		public Protocol.BattleFacingDirection FacingDirection => Info != null ? Info.FacingDirection : Protocol.BattleFacingDirection.None;
		public bool IsMoving { get; private set; }
		public IReadOnlyDictionary<Protocol.BattleResourceType, ResourceState> Resources => _resources;
		public IReadOnlyDictionary<ulong, BarrierState> Barriers => _barriers;
		public IReadOnlyDictionary<string, StatusState> Statuses => _statuses;
		public IReadOnlyDictionary<string, AuraState> Auras => _auras;
		public int TotalBarrierValue
		{
			get
			{
				long total = 0;
				foreach (BarrierState barrier in _barriers.Values)
					total += barrier.Value;

				return total > int.MaxValue ? int.MaxValue : (int)total;
			}
		}

		protected virtual void Awake()
		{
			_visualRoot = FindVisualRoot();
			_spriteRenderer = FindVisualSpriteRenderer();
			_animator = FindVisualAnimator();
			_projectileOrigin = FindProjectileOrigin();
			_statusWorldUi = GetComponentInChildren<PawnStatusWorldUI>(true);
			_teamRing = GetComponentInChildren<PawnTeamRing>(true);
		}

		protected virtual void OnDisable()
		{
			if (_moveCoroutine != null)
			{
				StopCoroutine(_moveCoroutine);
				_moveCoroutine = null;
			}

			if (_combatPresentationCoroutine != null)
			{
				StopCoroutine(_combatPresentationCoroutine);
				_combatPresentationCoroutine = null;
				if (_visualRoot != null)
					_visualRoot.localPosition = _combatPresentationBaseLocalPosition;
			}

			SetMoving(false);
			OnPawnDisabled();
		}

		public void Initialize(ulong pawnId, bool isMine, BattleMapGrid mapGrid, AxialCoord axial, Color tint, Protocol.BattlePawnInfo info = null)
		{
			PawnId = pawnId;
			IsMine = isMine;
			_mapGrid = mapGrid;
			Info = info?.Clone();
			ReplaceLocalStateCollections(
				Info?.Resources,
				Info?.Barriers,
				Info?.Statuses,
				Info?.Auras);

			_visualRoot = FindVisualRoot();
			_spriteRenderer = FindVisualSpriteRenderer();
			_animator = FindVisualAnimator();
			_projectileOrigin = FindProjectileOrigin();

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
			ApplyFacingDirection(FacingDirection);
			ApplyDeadVisualState();
			OnPawnInitialized();
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

			Vector3 target = _mapGrid.AxialToWorldCenter(axial, transform.position.z);
			UpdateFacingForMove(target);
			transform.position = target;
			OnAxialChanged();
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
			UpdateFacingForMove(target);

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

		void UpdateFacingForMove(Vector3 targetWorldPosition)
		{
			// Face the destination before the movement animation begins. The server's
			// facing value remains authoritative for combat/ZOC; this is presentation
			// only and prevents a visible turn after the pawn has already arrived.
			UpdateSpriteDirectionFromTarget(targetWorldPosition);
		}

		bool ApplyFacingDirection(Protocol.BattleFacingDirection direction)
		{
			if (_spriteRenderer == null)
				_spriteRenderer = FindVisualSpriteRenderer();

			if (_spriteRenderer == null)
				return false;

			switch (direction)
			{
				case Protocol.BattleFacingDirection.Left:
				case Protocol.BattleFacingDirection.QNegRPos:
				case Protocol.BattleFacingDirection.RNeg:
					_spriteRenderer.flipX = true;
					return true;
				case Protocol.BattleFacingDirection.Right:
				case Protocol.BattleFacingDirection.QPosRNeg:
				case Protocol.BattleFacingDirection.RPos:
					_spriteRenderer.flipX = false;
					return true;
				default:
					return false;
			}
		}

		void UpdateSpriteDirectionFromTarget(Vector3 targetWorldPosition)
		{
			if (_spriteRenderer == null)
				_spriteRenderer = FindVisualSpriteRenderer();

			if (_spriteRenderer == null)
				return;

			float deltaX = targetWorldPosition.x - transform.position.x;
			if (Mathf.Abs(deltaX) <= 0.001f)
				return;

			_spriteRenderer.flipX = deltaX < 0f;
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
			bool hasAxialDelta = delta.Axial != null;
			AxialCoord targetAxial = hasAxialDelta ? new AxialCoord(delta.Axial.Q, delta.Axial.R) : Axial;
			bool hasMoved = hasAxialDelta && Axial.Equals(targetAxial) == false;
			Info.Hp = delta.Hp;
			Info.Armor = delta.Armor;
			Info.CanMove = delta.CanMove;
			// Proto scalar fields have no presence bit. Older/partial pawn deltas omit
			// move_range as 0, which must not erase the range from the entry snapshot.
			// A real zero-move state is represented by CanMove on the server contract.
			if (delta.MoveRange > 0)
				Info.MoveRange = delta.MoveRange;
			Info.UsedNormalSkillThisTurn = delta.UsedNormalSkillThisTurn;
			Info.UsedSubActionThisTurn = delta.UsedSubActionThisTurn;
			Info.UsedUltimate = delta.UsedUltimate;
			Info.IsActionBlocked = delta.IsActionBlocked;
			Info.ZocReactionsUsedThisTurn = delta.ZocReactionsUsedThisTurn;
			Info.IsDead = delta.IsDead;
			Info.FacingDirection = delta.FacingDirection;
			Info.ShieldCurrent = delta.ShieldCurrent;
			Info.ShieldMax = delta.ShieldMax;
			ReplaceLocalStateCollections(delta.Resources, delta.Barriers, delta.Statuses, delta.Auras);
			ReplaceInfoStateCollections(delta.Resources, delta.Barriers, delta.Statuses, delta.Auras);

			ApplyFacingDirection(FacingDirection);
			// Position changes, including knockback and teleport, are delivered in the
			// authoritative pawn delta. Keep the old axial until MoveToAxial starts so
			// every server-driven displacement receives the normal movement animation.
			if (hasMoved)
				MoveToAxial(targetAxial);
			else if (hasAxialDelta)
				SetAxialState(targetAxial);
			RefreshStatusWorldUi();
			ApplyDeadVisualState();
			OnPawnStateChanged();
		}

		public void ApplyArmor(int armor)
		{
			EnsureInfo();
			Info.Armor = armor;
			RefreshStatusWorldUi();
		}

		// Combat logs are presented one at a time. Keep this separate from ApplyDelta:
		// the latter commits the complete server-authoritative snapshot after the
		// presentation sequence has finished.
		public void ApplyCombatLogPresentation(int hpAfter, int armorAfter)
		{
			EnsureInfo();
			int armorDelta = armorAfter - Info.Armor;
			Info.Hp = hpAfter;
			Info.Armor = armorAfter;
			// ShieldCurrent is the value used by the world/panel shield bar. Armor is
			// part of that aggregate, so reflect every log's ArmorAfter immediately
			// instead of waiting for the final pawn delta.
			if (Info.ShieldMax > 0)
				Info.ShieldCurrent = Mathf.Clamp(Info.ShieldCurrent + armorDelta, 0, Info.ShieldMax);

			RefreshStatusWorldUi();
		}

		public void PlayEvadePresentation(Vector3 attackerWorldPosition)
		{
			Vector3 direction = transform.position - attackerWorldPosition;
			direction.z = 0f;
			if (direction.sqrMagnitude <= 0.0001f)
				direction = FacingDirection == Protocol.BattleFacingDirection.Left ? Vector3.right : Vector3.left;

			// Use two separate hops: retreat, land, then hop back into the
			// counter-ready position. This reads more clearly than one smooth arc.
			PlayCombatPresentation(direction, EvadePresentationDurationSeconds, EvadePresentationDistance, EvadePresentationJumpHeight, true);
		}

		public void PlayMeleeAttackPresentation(Vector3 defenderWorldPosition)
		{
			Vector3 direction = defenderWorldPosition - transform.position;
			direction.z = 0f;
			if (direction.sqrMagnitude <= 0.0001f)
				direction = FacingDirection == Protocol.BattleFacingDirection.Left ? Vector3.left : Vector3.right;

			PlayCombatPresentation(direction, MeleePresentationDurationSeconds, MeleePresentationDistance, MeleePresentationHopHeight, false);
		}

		public Vector3 GetProjectileOriginWorldPosition()
		{
			if (_projectileOrigin == null)
				_projectileOrigin = FindProjectileOrigin();

			return _projectileOrigin != null
				? _projectileOrigin.position
				: transform.position + Vector3.up * FallbackProjectileOriginHeight;
		}

		public void PlayControlSuccessPresentation(string message)
		{
			if (IsDead || string.IsNullOrWhiteSpace(message))
				return;

			GameObject marker = new GameObject("ControlSuccess");
			marker.transform.SetParent(transform, false);
			marker.transform.localPosition = new Vector3(0f, GetStatusWorldUiHeight() + 0.28f, 0f);
			TextMesh text = marker.AddComponent<TextMesh>();
			text.text = message;
			text.anchor = TextAnchor.MiddleCenter;
			text.alignment = TextAlignment.Center;
			text.characterSize = 0.15f;
			text.fontSize = 34;
			text.color = new Color(1f, 0.78f, 0.2f, 1f);
			GameRoot.ApplyWorldTextFont(text);
			MeshRenderer renderer = marker.GetComponent<MeshRenderer>();
			if (renderer != null)
				renderer.sortingOrder = TurnIndicatorSortingOrder + 1;

			Destroy(marker, 0.65f);
		}

		void PlayCombatPresentation(Vector3 direction, float duration, float distance, float hopHeight, bool useReturnHop)
		{
			if (IsDead)
				return;

			if (_visualRoot == null)
				_visualRoot = FindVisualRoot();

			if (_visualRoot == null || gameObject.activeInHierarchy == false)
				return;

			if (_combatPresentationCoroutine != null)
			{
				StopCoroutine(_combatPresentationCoroutine);
				_visualRoot.localPosition = _combatPresentationBaseLocalPosition;
			}

			_combatPresentationBaseLocalPosition = _visualRoot.localPosition;
			_combatPresentationCoroutine = StartCoroutine(PlayCombatPresentationRoutine(direction.normalized, duration, distance, hopHeight, useReturnHop));
		}

		IEnumerator PlayCombatPresentationRoutine(Vector3 direction, float duration, float distance, float hopHeight, bool useReturnHop)
		{
			Vector3 baseWorldPosition = _visualRoot.position;
			float elapsed = 0f;
			while (elapsed < duration)
			{
				elapsed += Time.unscaledDeltaTime;
				float normalizedTime = Mathf.Clamp01(elapsed / duration);
				float distanceRatio;
				float hopRatio;
				if (useReturnHop)
				{
					// First half: hop backward and land. Second half: hop forward and land
					// at the original position, ready for the following counter-lunge.
					float phaseTime = normalizedTime <= 0.5f
						? normalizedTime * 2f
						: (normalizedTime - 0.5f) * 2f;
					float easedPhaseTime = phaseTime * phaseTime * (3f - 2f * phaseTime);
					distanceRatio = normalizedTime <= 0.5f ? easedPhaseTime : 1f - easedPhaseTime;
					hopRatio = Mathf.Sin(phaseTime * Mathf.PI);
				}
				else
				{
					distanceRatio = Mathf.Sin(normalizedTime * Mathf.PI);
					hopRatio = distanceRatio;
				}

				_visualRoot.position = baseWorldPosition + direction * (distanceRatio * distance) + Vector3.up * (hopRatio * hopHeight);
				yield return null;
			}

			if (_visualRoot != null)
				_visualRoot.localPosition = _combatPresentationBaseLocalPosition;

			_combatPresentationCoroutine = null;
		}

		public void ApplyDead(ulong killerPawnId)
		{
			EnsureInfo();
			Info.Hp = 0;
			Info.CanMove = false;
			Info.IsDead = true;
			SetTurnIndicatorVisible(false);
			RefreshStatusWorldUi();
			ApplyDeadVisualState();
			OnPawnStateChanged();
			Debug.Log($"Battle pawn dead. pawnId={PawnId}, killerPawnId={killerPawnId}");
		}

		void ReplaceLocalStateCollections(
			IEnumerable<Protocol.BattleResourceState> resources,
			IEnumerable<Protocol.BattleBarrierState> barriers,
			IEnumerable<Protocol.BattleStatusState> statuses,
			IEnumerable<Protocol.BattleAuraState> auras)
		{
			_resources.Clear();
			_barriers.Clear();
			_statuses.Clear();
			_auras.Clear();

			if (resources != null)
			{
				foreach (Protocol.BattleResourceState resource in resources)
				{
					if (resource == null || resource.ResourceType == Protocol.BattleResourceType.None)
						continue;

					_resources[resource.ResourceType] = new ResourceState(resource.ResourceType, resource.Value, resource.MaxValue);
				}
			}

			if (barriers != null)
			{
				foreach (Protocol.BattleBarrierState barrier in barriers)
				{
					if (barrier == null || barrier.BarrierId == 0)
						continue;

					_barriers[barrier.BarrierId] = new BarrierState(
						barrier.BarrierId,
						barrier.SourceSkillKey,
						barrier.Value,
						barrier.RemainingOwnerTurns,
						barrier.MaxValue);
				}
			}

			if (statuses != null)
			{
				foreach (Protocol.BattleStatusState status in statuses)
				{
					if (status == null || string.IsNullOrWhiteSpace(status.StatusKey))
						continue;

					_statuses[status.StatusKey] = new StatusState(status.StatusKey, status.Stacks, status.RemainingOwnerTurns);
				}
			}

			if (auras == null)
				return;

			foreach (Protocol.BattleAuraState aura in auras)
			{
				if (aura == null || string.IsNullOrWhiteSpace(aura.SourceSkillKey) || aura.Radius <= 0)
					continue;

				_auras[aura.SourceSkillKey] = new AuraState(aura.SourceSkillKey, aura.Radius);
			}
		}

		void ReplaceInfoStateCollections(
			IEnumerable<Protocol.BattleResourceState> resources,
			IEnumerable<Protocol.BattleBarrierState> barriers,
			IEnumerable<Protocol.BattleStatusState> statuses,
			IEnumerable<Protocol.BattleAuraState> auras)
		{
			if (Info == null)
				return;

			Info.Resources.Clear();
			Info.Barriers.Clear();
			Info.Statuses.Clear();
			Info.Auras.Clear();

			if (resources != null)
			{
				foreach (Protocol.BattleResourceState resource in resources)
				{
					if (resource != null)
						Info.Resources.Add(resource.Clone());
				}
			}

			if (barriers != null)
			{
				foreach (Protocol.BattleBarrierState barrier in barriers)
				{
					if (barrier != null)
						Info.Barriers.Add(barrier.Clone());
				}
			}

			if (statuses != null)
			{
				foreach (Protocol.BattleStatusState status in statuses)
				{
					if (status != null)
						Info.Statuses.Add(status.Clone());
				}
			}

			if (auras == null)
				return;

			foreach (Protocol.BattleAuraState aura in auras)
			{
				if (aura != null)
					Info.Auras.Add(aura.Clone());
			}
		}

		public virtual void TriggerSkill(int skillSlot)
		{
			switch (skillSlot)
			{
				case 2:
				case 3:
				case 4:
				case 5:
				case 6:
				case 7:
					TriggerSkill(DefaultSkillTrigger);
					break;
			}
		}

		public virtual void TriggerSkill(string triggerName)
		{
			if (IsDead)
				return;

			if (string.IsNullOrWhiteSpace(triggerName))
				return;

			if (_animator == null)
				_animator = FindVisualAnimator();

			if (_animator == null)
				return;

			int triggerHash = Animator.StringToHash(triggerName);
			if (HasTriggerParameter(_animator, triggerHash))
			{
				_animator.SetTrigger(triggerHash);
				return;
			}

			Debug.LogWarning($"Animator trigger not found. pawnId={PawnId}, trigger={triggerName}");
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
				CanMove = true,
				Role = Protocol.BattlePawnRole.Melee,
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
				Debug.LogWarning($"{nameof(BattlePawn)} requires a PawnTeamRing child prefab. pawnId={PawnId}, name={name}");
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
			GameRoot.ApplyWorldTextFont(textMesh);
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
				Debug.LogWarning($"{nameof(BattlePawn)} requires a PawnStatusWorldUI child prefab. pawnId={PawnId}, name={name}");
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

			_statusWorldUi.SetValues(Hp, MaxHp, ShieldCurrent, ShieldMax);
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

		Transform FindProjectileOrigin()
		{
			Transform[] transforms = GetComponentsInChildren<Transform>(true);
			for (int i = 0; i < transforms.Length; i++)
			{
				Transform candidate = transforms[i];
				if (candidate != null && candidate.name == ProjectileOriginName)
					return candidate;
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

		static bool HasTriggerParameter(Animator animator, int nameHash)
		{
			if (animator == null || animator.runtimeAnimatorController == null)
				return false;

			AnimatorControllerParameter[] parameters = animator.parameters;
			for (int i = 0; i < parameters.Length; i++)
			{
				AnimatorControllerParameter parameter = parameters[i];
				if (parameter.type == AnimatorControllerParameterType.Trigger && parameter.nameHash == nameHash)
					return true;
			}

			return false;
		}

		// Character families override these hooks for presentation and local-only behavior.
		// Authoritative combat values remain in the server snapshots handled above.
		protected BattleMapGrid MapGrid => _mapGrid;
		protected virtual void OnPawnInitialized() { }
		protected virtual void OnPawnStateChanged() { }
		protected virtual void OnAxialChanged() { }
		protected virtual void OnPawnDisabled() { }

		public readonly struct ResourceState
		{
			public Protocol.BattleResourceType ResourceType { get; }
			public int Value { get; }
			public int MaxValue { get; }

			public ResourceState(Protocol.BattleResourceType resourceType, int value, int maxValue)
			{
				ResourceType = resourceType;
				Value = value;
				MaxValue = maxValue;
			}
		}

		public readonly struct BarrierState
		{
			public ulong BarrierId { get; }
			public string SourceSkillKey { get; }
			public int Value { get; }
			public int RemainingOwnerTurns { get; }
			public int MaxValue { get; }

			public BarrierState(ulong barrierId, string sourceSkillKey, int value, int remainingOwnerTurns, int maxValue)
			{
				BarrierId = barrierId;
				SourceSkillKey = sourceSkillKey ?? string.Empty;
				Value = value;
				RemainingOwnerTurns = remainingOwnerTurns;
				MaxValue = maxValue;
			}
		}

		public readonly struct StatusState
		{
			public string StatusKey { get; }
			public int Stacks { get; }
			public int RemainingOwnerTurns { get; }

			public StatusState(string statusKey, int stacks, int remainingOwnerTurns)
			{
				StatusKey = statusKey ?? string.Empty;
				Stacks = stacks;
				RemainingOwnerTurns = remainingOwnerTurns;
			}
		}

		public readonly struct AuraState
		{
			public string SourceSkillKey { get; }
			public int Radius { get; }

			public AuraState(string sourceSkillKey, int radius)
			{
				SourceSkillKey = sourceSkillKey ?? string.Empty;
				Radius = radius;
			}
		}
	}
}
