using UnityEngine;
using UnityEngine.Tilemaps;

namespace Field
{
	[DisallowMultipleComponent]
	public sealed class FieldMapAxialCoordinates : MonoBehaviour
	{
		[SerializeField] Grid grid;
		[SerializeField] Tilemap groundTilemap;
		[SerializeField] Tilemap propTilemap;

		public Grid Grid => grid;
		public Tilemap GroundTilemap => groundTilemap;
		public Tilemap PropTilemap => propTilemap;

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

		public AxialCoord WorldToAxial(Vector3 worldPosition)
		{
			return AxialCoord.FromCell(WorldToCell(worldPosition));
		}

		public Vector3Int AxialToCell(AxialCoord axial)
		{
			return axial.ToCell();
		}

		public Vector3Int WorldToCell(Vector3 worldPosition)
		{
			EnsureGrid();
			return grid.WorldToCell(worldPosition);
		}

		public Vector3 AxialToWorld(AxialCoord axial)
		{
			EnsureGrid();
			return grid.GetCellCenterWorld(axial.ToCell());
		}

		public bool HasGroundTile(AxialCoord axial)
		{
			return groundTilemap != null && groundTilemap.HasTile(axial.ToCell());
		}

		public bool HasPropTile(AxialCoord axial)
		{
			return propTilemap != null && propTilemap.HasTile(axial.ToCell());
		}

		public bool IsInsideMap(AxialCoord axial)
		{
			return HasGroundTile(axial);
		}

		public bool IsBlocked(AxialCoord axial)
		{
			return HasPropTile(axial);
		}

		public bool IsWalkable(AxialCoord axial)
		{
			return IsInsideMap(axial) && IsBlocked(axial) == false;
		}

		public AxialCoord GetNeighbor(AxialCoord axial, int direction)
		{
			return axial.Neighbor(direction);
		}

		public int GetDistance(AxialCoord from, AxialCoord to)
		{
			return from.DistanceTo(to);
		}

		void EnsureGrid()
		{
			InitializeIfNeeded();

			if (grid == null)
				throw new MissingComponentException($"{nameof(FieldMapAxialCoordinates)} requires a Grid component.");
		}
	}
}
