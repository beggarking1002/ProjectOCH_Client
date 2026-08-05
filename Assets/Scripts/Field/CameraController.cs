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

		Vector3 _manualPanOffset;

		public Transform Target => target;

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

		void FindFieldPawnTarget()
		{
			FieldPawnController pawn = FindFirstObjectByType<FieldPawnController>();
			if (pawn != null)
				SetTarget(pawn.transform);
		}
	}
}
