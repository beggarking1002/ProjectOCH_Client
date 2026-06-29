using UnityEngine;
using UnityEngine.Tilemaps;

namespace Field
{
	[DisallowMultipleComponent]
	public sealed class FieldMapWalkArea : MonoBehaviour
	{
		[SerializeField] Grid grid;
		[SerializeField] Tilemap groundTilemap;
		[SerializeField] Tilemap blockTilemap;
		[SerializeField] bool useBlockTilemap;

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

			if (blockTilemap == null)
				blockTilemap = FindChildTilemap("Block_Tilemap", "Prop_Tilemap");
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

			return HasGroundTile(cell) && (useBlockTilemap == false || HasBlockTile(cell) == false);
		}

		bool HasGroundTile(Vector3Int cell)
		{
			return groundTilemap != null && groundTilemap.HasTile(cell);
		}

		bool HasBlockTile(Vector3Int cell)
		{
			return blockTilemap != null && blockTilemap.HasTile(cell);
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
				throw new MissingComponentException($"{nameof(FieldMapWalkArea)} requires a Grid component.");
		}
	}
}
