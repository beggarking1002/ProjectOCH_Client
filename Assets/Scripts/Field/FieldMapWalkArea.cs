using UnityEngine;
using UnityEngine.Tilemaps;

namespace Field
{
	[DisallowMultipleComponent]
	public sealed class FieldMapWalkArea : MonoBehaviour
	{
		[SerializeField] Grid grid;
		[SerializeField] Tilemap groundTilemap;
		[SerializeField] Tilemap propTilemap;

		public Transform PlaneTransform => transform;

		void Awake()
		{
			InitializeIfNeeded();
		}

		void OnValidate()
		{
			InitializeIfNeeded();
		}

		public void InitializeIfNeeded()
		{
			if (grid == null)
				grid = GetComponent<Grid>();

			if (groundTilemap == null)
			{
				Transform ground = transform.Find("Ground_Tilemap");
				if (ground != null)
					groundTilemap = ground.GetComponent<Tilemap>();
			}

			if (propTilemap == null)
			{
				Transform prop = transform.Find("Prop_Tilemap");
				if (prop != null)
					propTilemap = prop.GetComponent<Tilemap>();
			}
		}

		public Vector3 GetDefaultSpawnPosition(float z)
		{
			EnsureGrid();

			Vector3 position = grid.GetCellCenterWorld(Vector3Int.zero);
			position.z = z;
			return position;
		}

		public bool IsWalkable(Vector3 worldPosition)
		{
			EnsureGrid();
			Vector3Int cell = grid.WorldToCell(worldPosition);

			return HasGroundTile(cell) && HasPropTile(cell) == false;
		}

		bool HasGroundTile(Vector3Int cell)
		{
			return groundTilemap != null && groundTilemap.HasTile(cell);
		}

		bool HasPropTile(Vector3Int cell)
		{
			return propTilemap != null && propTilemap.HasTile(cell);
		}

		void EnsureGrid()
		{
			InitializeIfNeeded();

			if (grid == null)
				throw new MissingComponentException($"{nameof(FieldMapWalkArea)} requires a Grid component.");
		}
	}
}
