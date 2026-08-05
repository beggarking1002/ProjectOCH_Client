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
		[Header("Edge Scroll")]
		[SerializeField] bool edgeScrollEnabled = true;
		[SerializeField, Min(1f)] float edgeScrollPixels = 64f;
		[SerializeField, Min(0f)] float edgeScrollSpeed = 7f;

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

			Vector3 edgeScrollDelta = GetEdgeScrollDelta();
			if (target == null)
			{
				// Battle cameras have no follow target: pan their transform directly.
				transform.position += edgeScrollDelta;
				return;
			}

			_manualPanOffset += edgeScrollDelta;
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

		public void ConfigureFreePan()
		{
			target = null;
			findFieldPawnOnStart = false;
			_manualPanOffset = Vector3.zero;
		}

		Vector3 GetEdgeScrollDelta()
		{
			if (edgeScrollEnabled == false || Application.isFocused == false || TryGetPointerPosition(out Vector2 pointerPosition) == false)
				return Vector3.zero;

			Rect cameraPixels = _camera != null
				? _camera.pixelRect
				: new Rect(0f, 0f, Screen.width, Screen.height);
			float threshold = Mathf.Min(edgeScrollPixels, Mathf.Min(cameraPixels.width, cameraPixels.height) * 0.5f);
			Vector2 direction = Vector2.zero;
			if (pointerPosition.x <= cameraPixels.xMin + threshold)
				direction.x = -1f;
			else if (pointerPosition.x >= cameraPixels.xMax - threshold)
				direction.x = 1f;

			if (pointerPosition.y <= cameraPixels.yMin + threshold)
				direction.y = -1f;
			else if (pointerPosition.y >= cameraPixels.yMax - threshold)
				direction.y = 1f;

			if (direction == Vector2.zero)
				return Vector3.zero;

			direction.Normalize();
			return (Vector3)(direction * edgeScrollSpeed * Time.deltaTime);
		}

		static bool TryGetPointerPosition(out Vector2 screenPosition)
		{
#if ENABLE_INPUT_SYSTEM
			Mouse mouse = Mouse.current;
			if (mouse != null)
			{
				screenPosition = mouse.position.ReadValue();
				return true;
			}
#elif ENABLE_LEGACY_INPUT_MANAGER
			screenPosition = Input.mousePosition;
			return true;
#endif

			screenPosition = default;
			return false;
		}

		void FindFieldPawnTarget()
		{
			FieldPawnController pawn = FindFirstObjectByType<FieldPawnController>();
			if (pawn != null)
				SetTarget(pawn.transform);
		}
	}
}
