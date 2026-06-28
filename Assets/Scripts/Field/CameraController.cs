using UnityEngine;

namespace Field
{
	[DisallowMultipleComponent]
	public sealed class CameraController : MonoBehaviour
	{
		[SerializeField] Transform target;
		[SerializeField] Vector3 offset = new Vector3(0f, 0f, -10f);
		[SerializeField] float followSpeed = 8f;
		[SerializeField] bool findFieldPawnOnStart = true;

		public Transform Target => target;

		void Start()
		{
			if (target == null && findFieldPawnOnStart)
				FindFieldPawnTarget();
		}

		void LateUpdate()
		{
			if (target == null)
			{
				if (findFieldPawnOnStart)
					FindFieldPawnTarget();

				return;
			}

			Vector3 targetPosition = target.position + offset;
			transform.position = Vector3.Lerp(
				transform.position,
				targetPosition,
				1f - Mathf.Exp(-followSpeed * Time.deltaTime));
		}

		public void SetTarget(Transform followTarget)
		{
			target = followTarget;
			if (target != null)
				transform.position = target.position + offset;
		}

		void FindFieldPawnTarget()
		{
			FieldPawnController pawn = FindFirstObjectByType<FieldPawnController>();
			if (pawn != null)
				SetTarget(pawn.transform);
		}
	}
}
