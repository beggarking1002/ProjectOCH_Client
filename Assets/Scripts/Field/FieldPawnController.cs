using App;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Field
{
	[DisallowMultipleComponent]
	public sealed class FieldPawnController : MonoBehaviour
	{
		static readonly int IsMovingHash = Animator.StringToHash("IsMoving");

		[SerializeField] float moveSpeed = 4f;
		[SerializeField] float arriveDistance = 0.01f;

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

		public ulong ObjectId { get; private set; }
		public bool IsMine { get; private set; } = true;
		public bool IsMoving => _isMoving;

		void Awake()
		{
			_animator = GetComponentInChildren<Animator>();
			_spriteRenderer = GetComponentInChildren<SpriteRenderer>();
			SetMoving(false);
		}

		void Update()
		{
			HandleMouseInput();
			UpdateMovement();
		}

		public void Initialize(FieldMapWalkArea walkArea, Vector3 startWorldPosition, ulong objectId = 0, bool isMine = true)
		{
			_walkArea = walkArea;
			ObjectId = objectId;
			IsMine = isMine;
			_targetWorldPosition = startWorldPosition;
			transform.position = _targetWorldPosition;
			SetMoving(false);
		}

		void HandleMouseInput()
		{
			if (IsMine == false || _walkArea == null || TryGetPointerDown(out Vector2 screenPosition) == false)
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
			if (_walkArea.IsWalkable(worldPosition) == false)
				return;

			worldPosition.z = transform.position.z;
			MoveTo(worldPosition);
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

		void MoveTo(Vector3 targetWorldPosition)
		{
			if (Vector3.Distance(transform.position, targetWorldPosition) <= arriveDistance)
			{
				SetMoving(false);
				return;
			}

			_targetWorldPosition = targetWorldPosition;
			_useServerMoveDuration = false;
			UpdateSpriteDirection(_targetWorldPosition);
			SetMoving(true);
			Debug.Log($"Move pawn to world {_targetWorldPosition}");
			SendMovePacket(_targetWorldPosition);
		}

		public void SetWorldPosition(Vector3 worldPosition)
		{
			_targetWorldPosition = worldPosition;
			transform.position = worldPosition;
			_useServerMoveDuration = false;
			SetMoving(false);
		}

		public void ApplyServerMove(Vector3 startWorldPosition, Vector3 targetWorldPosition, uint durationMs, bool snapToStart)
		{
			if (snapToStart)
				transform.position = startWorldPosition;

			_serverMoveStartPosition = transform.position;
			_targetWorldPosition = targetWorldPosition;
			_serverMoveElapsed = 0f;
			_serverMoveDuration = durationMs / 1000f;
			_useServerMoveDuration = _serverMoveDuration > 0f;

			UpdateSpriteDirection(_targetWorldPosition);
			SetMoving(Vector3.Distance(transform.position, _targetWorldPosition) > arriveDistance);
		}

		void UpdateMovement()
		{
			if (_isMoving == false)
				return;

			if (_useServerMoveDuration)
			{
				_serverMoveElapsed += Time.deltaTime;
				float t = Mathf.Clamp01(_serverMoveElapsed / _serverMoveDuration);
				transform.position = Vector3.Lerp(_serverMoveStartPosition, _targetWorldPosition, t);
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
