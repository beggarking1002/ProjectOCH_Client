using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Field
{
	[DisallowMultipleComponent]
	public sealed class CameraController : MonoBehaviour
	{
		[SerializeField] Transform target;
		[SerializeField] Vector3 offset = new Vector3(0f, 0f, -10f);
		[SerializeField] float followSpeed = 8f;
		[SerializeField] bool findFieldPawnOnStart = true;
		[Header("Keyboard Pan")]
		[SerializeField] bool keyboardPanEnabled = true;
		[SerializeField, Min(0f)] float keyboardPanSpeed = 7f;
		[Header("Zoom")]
		[SerializeField] bool zoomEnabled = true;
		[SerializeField, Min(0.01f)] float zoomStepPerWheelNotch = 0.8f;
		[SerializeField, Min(0.01f)] float minOrthographicSize = 3f;
		[SerializeField, Min(0.01f)] float maxOrthographicSize = 12f;
		[SerializeField, Min(1f)] float minPerspectiveFieldOfView = 20f;
		[SerializeField, Min(1f)] float maxPerspectiveFieldOfView = 70f;

		Vector3 _manualPanOffset;
		Camera _camera;

		public Transform Target => target;

		void Awake()
		{
			_camera = GetComponent<Camera>();
		}

		void Start()
		{
			if (target == null && findFieldPawnOnStart)
				FindFieldPawnTarget();
		}

		void LateUpdate()
		{
			if (target == null)
				if (findFieldPawnOnStart)
					FindFieldPawnTarget();

			if (WasFocusKeyPressed())
				FocusTarget();
			ApplyZoomInput();

			Vector3 keyboardPanDelta = GetKeyboardPanDelta();
			if (target == null)
			{
				// Battle cameras have no follow target: pan their transform directly.
				transform.position += keyboardPanDelta;
				return;
			}

			_manualPanOffset += keyboardPanDelta;
			Vector3 targetPosition = target.position + offset + _manualPanOffset;
			transform.position = Vector3.Lerp(
				transform.position,
				targetPosition,
				1f - Mathf.Exp(-followSpeed * Time.deltaTime));
		}

		public void SetTarget(Transform followTarget, bool snapImmediately = true)
		{
			target = followTarget;
			_manualPanOffset = Vector3.zero;
			if (snapImmediately)
				SnapToTarget();
		}

		public void SnapToTarget()
		{
			if (target == null)
				return;

			transform.position = target.position + offset + _manualPanOffset;
		}

		public void ResetManualPan()
		{
			_manualPanOffset = Vector3.zero;
		}

		public void FocusTarget()
		{
			if (target == null && findFieldPawnOnStart)
				FindFieldPawnTarget();

			FocusOn(target);
		}

		public void FocusOn(Transform focusTarget)
		{
			if (focusTarget == null)
				return;

			_manualPanOffset = Vector3.zero;
			transform.position = focusTarget.position + offset;
		}

		public void ConfigureFreePan()
		{
			target = null;
			findFieldPawnOnStart = false;
			_manualPanOffset = Vector3.zero;
		}

		Vector3 GetKeyboardPanDelta()
		{
			if (keyboardPanEnabled == false || Application.isFocused == false)
				return Vector3.zero;

			Vector2 direction = Vector2.zero;

#if ENABLE_INPUT_SYSTEM
			Keyboard keyboard = Keyboard.current;
			if (keyboard == null)
				return Vector3.zero;

			if (keyboard.aKey.isPressed)
				direction.x -= 1f;
			if (keyboard.dKey.isPressed)
				direction.x += 1f;
			if (keyboard.sKey.isPressed)
				direction.y -= 1f;
			if (keyboard.wKey.isPressed)
				direction.y += 1f;
#elif ENABLE_LEGACY_INPUT_MANAGER
			if (Input.GetKey(KeyCode.A))
				direction.x -= 1f;
			if (Input.GetKey(KeyCode.D))
				direction.x += 1f;
			if (Input.GetKey(KeyCode.S))
				direction.y -= 1f;
			if (Input.GetKey(KeyCode.W))
				direction.y += 1f;
#endif

			if (direction == Vector2.zero)
				return Vector3.zero;

			direction.Normalize();
			return (Vector3)(direction * keyboardPanSpeed * Time.deltaTime);
		}

		void ApplyZoomInput()
		{
			if (zoomEnabled == false || Application.isFocused == false || _camera == null)
				return;

			float scrollY = GetMouseScrollY();
			if (Mathf.Approximately(scrollY, 0f))
				return;

			float zoomDelta = Mathf.Clamp(scrollY / 120f, -3f, 3f) * zoomStepPerWheelNotch;
			if (_camera.orthographic)
				_camera.orthographicSize = Mathf.Clamp(_camera.orthographicSize - zoomDelta, minOrthographicSize, maxOrthographicSize);
			else
				_camera.fieldOfView = Mathf.Clamp(_camera.fieldOfView - zoomDelta, minPerspectiveFieldOfView, maxPerspectiveFieldOfView);
		}

		static bool WasFocusKeyPressed()
		{
#if ENABLE_INPUT_SYSTEM
			Keyboard keyboard = Keyboard.current;
			return keyboard != null && keyboard.spaceKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
			return Input.GetKeyDown(KeyCode.Space);
#else
			return false;
#endif
		}

		static float GetMouseScrollY()
		{
#if ENABLE_INPUT_SYSTEM
			Mouse mouse = Mouse.current;
			return mouse != null ? mouse.scroll.ReadValue().y : 0f;
#elif ENABLE_LEGACY_INPUT_MANAGER
			return Input.mouseScrollDelta.y * 120f;
#else
			return 0f;
#endif
		}

		void FindFieldPawnTarget()
		{
			FieldPawnController pawn = FindFirstObjectByType<FieldPawnController>();
			if (pawn != null)
				SetTarget(pawn.transform);
		}
	}
}
