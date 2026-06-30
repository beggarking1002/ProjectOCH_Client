using UnityEngine;
using UnityEngine.Tilemaps;

namespace Battle
{
	[DisallowMultipleComponent]
	public sealed class BattleMapGrid : MonoBehaviour
	{
		[SerializeField] Grid grid;
		[SerializeField] Tilemap groundTilemap;
		[SerializeField] Tilemap blockTilemap;
		[SerializeField] bool useBlockTilemap = true;

		public Transform PlaneTransform => transform;
		public Grid Grid => grid;

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
				groundTilemap = FindChildTilemap("Ground_Tilemap");

			if (blockTilemap == null)
				blockTilemap = FindChildTilemap("Block_Tilemap", "Prop_Tilemap");
		}

		public AxialCoord WorldToAxial(Vector3 worldPosition)
		{
			EnsureGrid();
			return AxialCoord.FromCell(grid.WorldToCell(worldPosition));
		}

		public Vector3 AxialToWorldCenter(AxialCoord axial, float z = 0f)
		{
			EnsureGrid();
			Vector3 position = grid.GetCellCenterWorld(axial.ToCell());
			position.z = z;
			return position;
		}

		public bool HasGroundTile(AxialCoord axial)
		{
			return groundTilemap != null && groundTilemap.HasTile(axial.ToCell());
		}

		public bool HasBlockTile(AxialCoord axial)
		{
			return blockTilemap != null && blockTilemap.HasTile(axial.ToCell());
		}

		public bool IsWalkable(AxialCoord axial)
		{
			if (HasGroundTile(axial) == false)
				return false;

			return useBlockTilemap == false || HasBlockTile(axial) == false;
		}

		public AxialCoord GetNeighbor(AxialCoord axial, int direction)
		{
			return axial.Neighbor(direction);
		}

		public int GetDistance(AxialCoord from, AxialCoord to)
		{
			return from.DistanceTo(to);
		}

		Tilemap FindChildTilemap(params string[] names)
		{
			for (int i = 0; i < names.Length; i++)
			{
				Transform child = transform.Find(names[i]);
				if (child == null)
					continue;

				Tilemap tilemap = child.GetComponent<Tilemap>();
				if (tilemap != null)
					return tilemap;
			}

			return null;
		}

		void EnsureGrid()
		{
			InitializeIfNeeded();

			if (grid == null)
				throw new MissingComponentException($"{nameof(BattleMapGrid)} requires a Grid component.");
		}
	}
}
