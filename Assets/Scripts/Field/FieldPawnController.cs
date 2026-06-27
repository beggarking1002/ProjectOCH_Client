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

		FieldMapAxialCoordinates _coordinates;
		Animator _animator;
		SpriteRenderer _spriteRenderer;
		AxialCoord _currentAxial;
		AxialCoord _targetAxial;
		Vector3 _targetWorldPosition;
		bool _isMoving;
		bool _missingCameraLogged;

		public AxialCoord CurrentAxial => _currentAxial;
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

		public void Initialize(FieldMapAxialCoordinates coordinates, AxialCoord startAxial)
		{
			_coordinates = coordinates;
			_currentAxial = startAxial;
			_targetAxial = startAxial;
			_targetWorldPosition = GetWorldPosition(startAxial);
			transform.position = _targetWorldPosition;
			SetMoving(false);
		}

		void HandleMouseInput()
		{
			if (_coordinates == null || TryGetPointerDown(out Vector2 screenPosition) == false)
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
			Plane mapPlane = new Plane(Vector3.forward, _coordinates.transform.position);
			if (mapPlane.Raycast(ray, out float enter) == false)
				return;

			Vector3 worldPosition = ray.GetPoint(enter);
			AxialCoord clickedAxial = _coordinates.WorldToAxial(worldPosition);
			if (_coordinates.IsWalkable(clickedAxial) == false)
				return;

			MoveTo(clickedAxial);
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

		void MoveTo(AxialCoord targetAxial)
		{
			if (targetAxial == _currentAxial)
			{
				SetMoving(false);
				return;
			}

			_targetAxial = targetAxial;
			_targetWorldPosition = GetWorldPosition(targetAxial);
			UpdateSpriteDirection(_targetWorldPosition);
			SetMoving(true);
			Debug.Log($"Move pawn to axial {_targetAxial}");
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
			_currentAxial = _targetAxial;
			SetMoving(false);
		}

		Vector3 GetWorldPosition(AxialCoord axial)
		{
			if (_coordinates == null)
				return transform.position;

			Vector3 position = _coordinates.AxialToWorld(axial);
			position.z = transform.position.z;
			return position;
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
	}
}
