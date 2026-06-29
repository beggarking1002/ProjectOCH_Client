using UnityEngine;

namespace Field
{
	public static class FieldPositionCodec
	{
		public const int FixedPointScale = 100;

		public static int ToFixed(float value)
		{
			return Mathf.RoundToInt(value * FixedPointScale);
		}

		public static Protocol.Vec2Fixed ToFixed(Vector3 position)
		{
			return new Protocol.Vec2Fixed
			{
				X = ToFixed(position.x),
				Y = ToFixed(position.y),
			};
		}

		public static Vector3 ToWorld(Protocol.Vec2Fixed position, float z)
		{
			if (position == null)
				return new Vector3(0f, 0f, z);

			return new Vector3(
				position.X / (float)FixedPointScale,
				position.Y / (float)FixedPointScale,
				z);
		}
	}
}
