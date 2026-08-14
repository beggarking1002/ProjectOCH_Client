using UnityEngine;
using UnityEngine.Tilemaps;

namespace Field
{
	[CreateAssetMenu(fileName = "VillageMarker", menuName = "Project OCH/Field/Village Marker Tile")]
	public sealed class FieldVillageMarkerTile : Tile
	{
		[SerializeField] string villageId;

		public string VillageId => villageId;
	}
}
