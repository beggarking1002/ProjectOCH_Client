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
		const int FixedPointScale = 100;

		[SerializeField] float moveSpeed = 4f;
		[SerializeField] float arriveDistance = 0.01f;

		FieldMapWalkArea _walkArea;
		Animator _animator;
		SpriteRenderer _spriteRenderer;
		Vector3 _targetWorldPosition;
		bool _isMoving;
		bool _missingCameraLogged;

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

		public void Initialize(FieldMapWalkArea walkArea, Vector3 startWorldPosition)
		{
			_walkArea = walkArea;
			_targetWorldPosition = startWorldPosition;
			transform.position = _targetWorldPosition;
			SetMoving(false);
		}

		void HandleMouseInput()
		{
			if (_walkArea == null || TryGetPointerDown(out Vector2 screenPosition) == false)
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
			UpdateSpriteDirection(_targetWorldPosition);
			SetMoving(true);
			Debug.Log($"Move pawn to world {_targetWorldPosition}");
			SendMovePacket(_targetWorldPosition);
		}

		void UpdateMovement()
		{
			if (_isMoving == false)
				return;

			transform.position = Vector3.MoveTowards(
				transform.position,
				_targetWorldPosition,
				moveSpeed * Time.deltaTime);

			if (Vector3.Distance(transform.position, _targetWorldPosition) > arriveDistance)
				return;

			transform.position = _targetWorldPosition;
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
				Target = new Protocol.Vec2Fixed
				{
					X = ToFixed(targetWorldPosition.x),
					Y = ToFixed(targetWorldPosition.y),
				},
			};

			if (GameRoot.Instance == null || GameRoot.Instance.Network.Send(packet) == false)
				Debug.LogWarning($"Failed to send C_MOVE target={targetWorldPosition}");
		}

		static int ToFixed(float value)
		{
			return Mathf.RoundToInt(value * FixedPointScale);
		}
	}
}
