using System.Collections.Generic;
using App;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Field
{
	[DisallowMultipleComponent]
	public sealed class FieldPawnController : MonoBehaviour
	{
		static readonly int IsMovingHash = Animator.StringToHash("isMoving");
		// Field_001 spans roughly world-Y -15..15. Keep the whole dynamic depth
		// range above the world-map renderers (0..4), then sort only pawns within
		// that safe foreground band.
		const int DefaultSortingOrder = 60;
		const float SortingOrderPerWorldYUnit = 2f;

		[SerializeField] float moveSpeed = 4f;
		[SerializeField] float arriveDistance = 0.01f;
		[SerializeField] bool validateLocallyBeforeSend = true;
		[SerializeField] bool sendBlockedMoveForDebug;

		FieldMapWalkArea _walkArea;
		Animator _animator;
		SpriteRenderer _spriteRenderer;
		Vector3 _targetWorldPosition;
		Vector3 _serverMoveStartPosition;
		bool _isMoving;
		bool _missingCameraLogged;
		bool _useServerMoveDuration;
		float _serverMoveElapsed;
		float _serverMoveDuration;
		float _serverMovePathLength;
		readonly List<Vector3> _serverMovePath = new List<Vector3>();

		public ulong ObjectId { get; private set; }
		public bool IsMine { get; private set; } = true;
		public bool IsMoving => _isMoving;

		void Awake()
		{
			_animator = GetComponentInChildren<Animator>();
			_spriteRenderer = GetComponentInChildren<SpriteRenderer>();
			ApplyRenderSettings();
			SetMoving(false);
		}

		void Update()
		{
			HandleMouseInput();
			UpdateMovement();
			ApplyRenderSettings();
		}

		public void Initialize(FieldMapWalkArea walkArea, Vector3 startWorldPosition, ulong objectId = 0, bool isMine = true)
		{
			_walkArea = walkArea;
			ObjectId = objectId;
			IsMine = isMine;
			_targetWorldPosition = startWorldPosition;
			transform.position = _targetWorldPosition;
			ApplyRenderSettings();
			SetMoving(false);
		}

		void ApplyRenderSettings()
		{
			if (_spriteRenderer == null)
				_spriteRenderer = GetComponentInChildren<SpriteRenderer>();

			if (_spriteRenderer != null)
				_spriteRenderer.sortingOrder = DefaultSortingOrder - Mathf.RoundToInt(transform.position.y * SortingOrderPerWorldYUnit);
		}

		void HandleMouseInput()
		{
			if (IsMine == false || _walkArea == null || TryGetPointerDown(out Vector2 screenPosition) == false)
				return;

			if (FieldPointerInputBlocker.IsConsumedThisFrame || FieldBattleInviteUI.IsBlockingInput || FieldBattleClassSelectionUI.IsBlockingInput || IsPointerOverUi())
				return;

			Camera camera = Camera.main;
			if (camera == null)
			{
				if (_missingCameraLogged == false)
				{
					_missingCameraLogged = true;
					Debug.LogWarning($"{nameof(FieldPawnController)} requires a MainCamera to convert mouse input.");
				}

				return;
			}

			if (IsValidScreenPosition(camera, screenPosition) == false)
				return;

			Ray ray = camera.ScreenPointToRay(screenPosition);
			Plane mapPlane = new Plane(Vector3.forward, _walkArea.PlaneTransform.position);
			if (mapPlane.Raycast(ray, out float enter) == false)
				return;

			Vector3 worldPosition = ray.GetPoint(enter);
			worldPosition.z = transform.position.z;
			bool walkable = _walkArea.IsWalkable(worldPosition);
			if (validateLocallyBeforeSend && walkable == false)
			{
				if (sendBlockedMoveForDebug)
				{
					Debug.Log($"Request blocked pawn move for server validation. world={worldPosition}");
					SendMovePacket(worldPosition);
				}

				return;
			}

			RequestMove(worldPosition);
		}

		bool TryGetPointerDown(out Vector2 screenPosition)
		{
#if ENABLE_INPUT_SYSTEM
			Mouse mouse = Mouse.current;
			if (mouse != null && mouse.leftButton.wasPressedThisFrame)
			{
				screenPosition = mouse.position.ReadValue();
				return true;
			}
#elif ENABLE_LEGACY_INPUT_MANAGER
			if (Input.GetMouseButtonDown(0))
			{
				screenPosition = Input.mousePosition;
				return true;
			}
#endif
			screenPosition = default;
			return false;
		}

		bool IsValidScreenPosition(Camera camera, Vector2 screenPosition)
		{
			if (float.IsNaN(screenPosition.x) || float.IsNaN(screenPosition.y))
				return false;

			if (float.IsInfinity(screenPosition.x) || float.IsInfinity(screenPosition.y))
				return false;

			return screenPosition.x >= 0f
				&& screenPosition.y >= 0f
				&& screenPosition.x <= camera.pixelWidth
				&& screenPosition.y <= camera.pixelHeight;
		}

		static bool IsPointerOverUi()
		{
			return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
		}

		void RequestMove(Vector3 targetWorldPosition)
		{
			if (Vector3.Distance(transform.position, targetWorldPosition) <= arriveDistance)
			{
				SetMoving(false);
				return;
			}

			Debug.Log($"Request pawn move to world {targetWorldPosition}");
			SendMovePacket(targetWorldPosition);
		}

		public void SetWorldPosition(Vector3 worldPosition)
		{
			_targetWorldPosition = worldPosition;
			transform.position = worldPosition;
			_useServerMoveDuration = false;
			_serverMovePath.Clear();
			_serverMovePathLength = 0f;
			SetMoving(false);
		}

		public void ApplyServerMove(Vector3 startWorldPosition, Vector3 targetWorldPosition, IList<Vector3> serverPath, uint durationMs, bool snapToStart)
		{
			if (snapToStart)
				transform.position = startWorldPosition;

			List<Vector3> nextPath = new List<Vector3>();
			bool appendToActiveRoute = snapToStart == false && _isMoving && _useServerMoveDuration &&
				Vector3.Distance(startWorldPosition, _targetWorldPosition) <= arriveDistance;
			if (appendToActiveRoute)
				AppendRemainingPath(nextPath);

			AppendPath(nextPath, serverPath, targetWorldPosition);
			_serverMoveStartPosition = transform.position;
			_serverMovePath.Clear();
			_serverMovePath.AddRange(nextPath);
			_targetWorldPosition = _serverMovePath.Count > 0 ? _serverMovePath[_serverMovePath.Count - 1] : targetWorldPosition;
			_serverMovePathLength = GetPathLength(_serverMoveStartPosition, _serverMovePath);
			_serverMoveElapsed = 0f;
			_serverMoveDuration = durationMs / 1000f;
			if (_serverMoveDuration > 0f)
			{
				float serverPathLength = GetPathLength(startWorldPosition, serverPath);
				if (serverPathLength > arriveDistance && _serverMovePathLength > arriveDistance)
				{
					float serverSpeed = serverPathLength / _serverMoveDuration;
					_serverMoveDuration = _serverMovePathLength / serverSpeed;
				}
			}
			_useServerMoveDuration = _serverMoveDuration > 0f;

			UpdateSpriteDirection(_targetWorldPosition);
			SetMoving(_serverMovePathLength > arriveDistance);
		}

		void AppendRemainingPath(List<Vector3> destination)
		{
			float travelledDistance = _serverMoveDuration > 0f
				? _serverMovePathLength * Mathf.Clamp01(_serverMoveElapsed / _serverMoveDuration)
				: 0f;
			Vector3 segmentStart = _serverMoveStartPosition;
			for (int i = 0; i < _serverMovePath.Count; i++)
			{
				Vector3 waypoint = _serverMovePath[i];
				float segmentLength = Vector3.Distance(segmentStart, waypoint);
				if (travelledDistance <= segmentLength)
				{
					destination.Add(waypoint);
					for (int remainingIndex = i + 1; remainingIndex < _serverMovePath.Count; remainingIndex++)
						destination.Add(_serverMovePath[remainingIndex]);
					return;
				}

				travelledDistance -= segmentLength;
				segmentStart = waypoint;
			}
		}

		void AppendPath(List<Vector3> destination, IList<Vector3> serverPath, Vector3 targetWorldPosition)
		{
			if (serverPath != null)
			{
				for (int i = 0; i < serverPath.Count; i++)
				{
					Vector3 previous = destination.Count > 0 ? destination[destination.Count - 1] : transform.position;
					if (Vector3.Distance(previous, serverPath[i]) > arriveDistance)
						destination.Add(serverPath[i]);
				}
			}

			Vector3 finalPrevious = destination.Count > 0 ? destination[destination.Count - 1] : transform.position;
			if (Vector3.Distance(finalPrevious, targetWorldPosition) > arriveDistance)
				destination.Add(targetWorldPosition);
		}

		static float GetPathLength(Vector3 start, IList<Vector3> path)
		{
			float length = 0f;
			Vector3 previous = start;
			if (path == null)
				return length;

			for (int i = 0; i < path.Count; i++)
			{
				length += Vector3.Distance(previous, path[i]);
				previous = path[i];
			}

			return length;
		}

		void ApplyPathPosition(float travelledDistance)
		{
			Vector3 segmentStart = _serverMoveStartPosition;
			for (int i = 0; i < _serverMovePath.Count; i++)
			{
				Vector3 waypoint = _serverMovePath[i];
				float segmentLength = Vector3.Distance(segmentStart, waypoint);
				if (travelledDistance <= segmentLength || i == _serverMovePath.Count - 1)
				{
					float segmentT = segmentLength <= 0f ? 1f : Mathf.Clamp01(travelledDistance / segmentLength);
					transform.position = Vector3.Lerp(segmentStart, waypoint, segmentT);
					return;
				}

				travelledDistance -= segmentLength;
				segmentStart = waypoint;
			}

			transform.position = _targetWorldPosition;
		}

		void UpdateMovement()
		{
			if (_isMoving == false)
				return;

			if (_useServerMoveDuration)
			{
				_serverMoveElapsed += Time.deltaTime;
				float t = Mathf.Clamp01(_serverMoveElapsed / _serverMoveDuration);
				ApplyPathPosition(_serverMovePathLength * t);
			}
			else
			{
				transform.position = Vector3.MoveTowards(
					transform.position,
					_targetWorldPosition,
					moveSpeed * Time.deltaTime);
			}

			if (Vector3.Distance(transform.position, _targetWorldPosition) > arriveDistance)
				return;

			transform.position = _targetWorldPosition;
			_useServerMoveDuration = false;
			_serverMovePath.Clear();
			_serverMovePathLength = 0f;
			SetMoving(false);
		}

		void SetMoving(bool isMoving)
		{
			_isMoving = isMoving;
			if (_animator != null)
				_animator.SetBool(IsMovingHash, isMoving);
		}

		void UpdateSpriteDirection(Vector3 targetWorldPosition)
		{
			if (_spriteRenderer == null)
				return;

			float deltaX = targetWorldPosition.x - transform.position.x;
			if (Mathf.Approximately(deltaX, 0f))
				return;

			_spriteRenderer.flipX = deltaX < 0f;
		}

		void SendMovePacket(Vector3 targetWorldPosition)
		{
			Protocol.C_MOVE packet = new Protocol.C_MOVE
			{
				Target = FieldPositionCodec.ToFixed(targetWorldPosition),
			};

			if (GameRoot.Instance == null || GameRoot.Instance.Network.Send(packet) == false)
				Debug.LogWarning($"Failed to send C_MOVE target={targetWorldPosition}");
		}
	}
}
