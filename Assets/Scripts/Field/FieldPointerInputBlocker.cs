using UnityEngine;

namespace Field
{
	internal static class FieldPointerInputBlocker
	{
		static int _consumedFrame = -1;

		public static bool IsConsumedThisFrame => _consumedFrame == Time.frameCount;

		public static void ConsumeCurrentFrame()
		{
			_consumedFrame = Time.frameCount;
		}
	}
}
