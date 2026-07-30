using System.Collections.Generic;
using System.Threading.Tasks;
using App;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.Serialization;
using UnityEngine.Tilemaps;

namespace Battle
{
	[DisallowMultipleComponent]
	public sealed class BattleMapGrid : MonoBehaviour
	{
		[SerializeField] Grid grid;
		[SerializeField] Tilemap groundTilemap;
		[FormerlySerializedAs("propTilemap")]
		[SerializeField] Tilemap blockTilemap;
		[SerializeField] Tilemap combatOverlayTilemap;
		[SerializeField] TileBase normalGroundTile;
		[SerializeField] TileBase waterGroundTile;
		[SerializeField] TileBase iceOverlayTile;
		[SerializeField] TileBase fireOverlayTile;
		[SerializeField] bool useBlockTilemap = true;
		readonly Dictionary<AxialCoord, BattleTileState> _serverTileStates = new Dictionary<AxialCoord, BattleTileState>();
		readonly Dictionary<AxialCoord, TextMesh> _equipmentMarkers = new Dictionary<AxialCoord, TextMesh>();
		static Sprite _equipmentMarkerPlateSprite;
		readonly List<AsyncOperationHandle<TileBase>> _loadedTileHandles = new List<AsyncOperationHandle<TileBase>>();
		bool _hasServerTileSnapshot;

		const string NormalGroundTileAddress = "Tile/grass";
		const string WaterGroundTileAddress = "Tile/water";
		const string IceOverlayTileAddress = "Tile/ice";
		const string FireOverlayTileAddress = "Tile/fire";

		public Transform PlaneTransform => transform;
		public Grid Grid => grid;
		public Tilemap CombatOverlayTilemap => combatOverlayTilemap;
		public bool HasServerTileSnapshot => _hasServerTileSnapshot;

		void Awake()
		{
			InitializeIfNeeded();
		}

		void OnValidate()
		{
			InitializeIfNeeded();
		}

		void OnDestroy()
		{
			for (int i = 0; i < _loadedTileHandles.Count; i++)
			{
				if (_loadedTileHandles[i].IsValid())
					Addressables.Release(_loadedTileHandles[i]);
			}

			_loadedTileHandles.Clear();
		}

		public void InitializeIfNeeded()
		{
			if (grid == null)
				grid = GetComponent<Grid>();

			if (groundTilemap == null)
				groundTilemap = FindChildTilemap("Ground_Tilemap");

			if (blockTilemap == null)
				blockTilemap = FindChildTilemap("Block_Tilemap", "Prop_Tilemap");

			if (combatOverlayTilemap == null)
				combatOverlayTilemap = FindChildTilemap("CombatOverlay_Tilemap");

			ResolveTileAssetsFromAuthoredGround();
		}

		// Tile assets are loaded by address as well as serialized on the prefab.
		// This keeps map presentation valid when an Addressables map bundle was
		// built before the serialized TileBase fields were added.
		public async Task LoadVisualTilesAsync()
		{
			InitializeIfNeeded();

			if (normalGroundTile == null)
				normalGroundTile = await LoadTileAsync(NormalGroundTileAddress);

			if (waterGroundTile == null)
				waterGroundTile = await LoadTileAsync(WaterGroundTileAddress);

			if (iceOverlayTile == null)
				iceOverlayTile = await LoadTileAsync(IceOverlayTileAddress);

			if (fireOverlayTile == null)
				fireOverlayTile = await LoadTileAsync(FireOverlayTileAddress);
		}

		public AxialCoord WorldToAxial(Vector3 worldPosition)
		{
			EnsureGrid();
			return CellToAxial(grid.WorldToCell(worldPosition));
		}

		public Vector3 AxialToWorldCenter(AxialCoord axial, float z = 0f)
		{
			EnsureGrid();
			Vector3 position = grid.GetCellCenterWorld(AxialToCell(axial));
			position.z = z;
			return position;
		}

		// Battle protocol coordinates are point-top, odd-r axial values. Unity's
		// Hexagon Grid stores the same map as offset cell coordinates: odd rows are
		// shifted right. Keep this conversion exclusively at the client map boundary.
		public static AxialCoord CellToAxial(Vector3Int cell)
		{
			int row = cell.y;
			int q = cell.x - ((row - (row & 1)) / 2);
			return new AxialCoord(q, row);
		}

		public static Vector3Int AxialToCell(AxialCoord axial, int z = 0)
		{
			int row = axial.R;
			int column = axial.Q + ((row - (row & 1)) / 2);
			return new Vector3Int(column, row, z);
		}

		public bool HasGroundTile(AxialCoord axial)
		{
			return groundTilemap != null && groundTilemap.HasTile(AxialToCell(axial));
		}

		public bool HasBlockTile(AxialCoord axial)
		{
			return blockTilemap != null && blockTilemap.HasTile(AxialToCell(axial));
		}

		public bool IsTileInBounds(AxialCoord axial)
		{
			return _hasServerTileSnapshot
				? _serverTileStates.ContainsKey(axial)
				: HasGroundTile(axial);
		}

		public bool HasOverlay(AxialCoord axial, Protocol.BattleTileOverlayType overlayType)
		{
			return overlayType != Protocol.BattleTileOverlayType.None
				&& _serverTileStates.TryGetValue(axial, out BattleTileState tileState)
				&& tileState.OverlayType == overlayType;
		}

		public bool HasEquipment(AxialCoord axial, string equipmentKey, ulong ownerPawnId)
		{
			return _serverTileStates.TryGetValue(axial, out BattleTileState tileState)
				&& string.Equals(tileState.EquipmentKey, equipmentKey, System.StringComparison.OrdinalIgnoreCase)
				&& tileState.EquipmentOwnerPawnId == ownerPawnId;
		}

		public bool TryGetEquipment(AxialCoord axial, out string equipmentKey, out ulong ownerPawnId)
		{
			equipmentKey = string.Empty;
			ownerPawnId = 0;
			if (_serverTileStates.TryGetValue(axial, out BattleTileState tileState) == false
				|| string.IsNullOrWhiteSpace(tileState.EquipmentKey))
			{
				return false;
			}

			equipmentKey = tileState.EquipmentKey;
			ownerPawnId = tileState.EquipmentOwnerPawnId;
			return true;
		}

		public void GetKnownTileAxials(List<AxialCoord> destination)
		{
			if (destination == null)
				return;

			destination.Clear();
			if (_hasServerTileSnapshot)
			{
				foreach (AxialCoord axial in _serverTileStates.Keys)
					destination.Add(axial);
				return;
			}

			if (groundTilemap == null)
				return;

			foreach (Vector3Int cell in groundTilemap.cellBounds.allPositionsWithin)
			{
				if (groundTilemap.HasTile(cell))
					destination.Add(CellToAxial(cell));
			}
		}

		public bool IsWalkable(AxialCoord axial)
		{
			if (IsWalkableByServerTileState(axial) == false)
				return false;

			return useBlockTilemap == false || HasBlockTile(axial) == false;
		}

		bool IsWalkableByServerTileState(AxialCoord axial)
		{
			if (_hasServerTileSnapshot == false)
				return HasGroundTile(axial);

			if (_serverTileStates.TryGetValue(axial, out BattleTileState tileState) == false)
				return false;

			// A deployed Parvis is terrain equipment: players cannot move or be pushed
			// through it. The server remains authoritative for every final path result.
			if (string.Equals(tileState.EquipmentKey, "PARVIS", System.StringComparison.OrdinalIgnoreCase))
				return false;

			switch (tileState.TileType)
			{
				case Protocol.BattleTileType.Normal:
					return true;
				case Protocol.BattleTileType.Water:
					return tileState.OverlayType == Protocol.BattleTileOverlayType.Ice;
				default:
					return false;
			}
		}

		public void ApplyTileSnapshot(IEnumerable<Protocol.BattleTileInfo> tiles)
		{
			if (tiles == null)
				return;

			List<Protocol.BattleTileInfo> tileSnapshot = new List<Protocol.BattleTileInfo>();
			foreach (Protocol.BattleTileInfo tile in tiles)
			{
				if (tile?.Axial != null)
					tileSnapshot.Add(tile);
			}

			// Keep the authored map intact when connected to an older server that does not
			// include the newly added tiles field.
			if (tileSnapshot.Count == 0)
				return;

			InitializeIfNeeded();

			// The walkmap JSON describes the server-authoritative valid-cell set, not
			// every authored ground or prop cell. Keep the prefab's static terrain so
			// non-walkable cells (such as the ground beneath trees) remain visible.
			_serverTileStates.Clear();
			ClearEquipmentMarkers();

			combatOverlayTilemap?.ClearAllTiles();

			for (int i = 0; i < tileSnapshot.Count; i++)
				ApplyTileState(tileSnapshot[i], updateGroundVisual: false);

			_hasServerTileSnapshot = true;
		}

		public void ApplyTileDeltas(IEnumerable<Protocol.BattleTileInfo> tileDeltas)
		{
			if (tileDeltas == null)
				return;

			foreach (Protocol.BattleTileInfo tileDelta in tileDeltas)
			{
				if (tileDelta?.Axial == null)
					continue;

				// A Delta updates state and the dynamic overlay only. Ground and Prop are
				// authored/static layers and must not be changed by a skill response.
				ApplyTileState(tileDelta, updateGroundVisual: false);
			}
		}

		void ApplyTileState(Protocol.BattleTileInfo tileInfo, bool updateGroundVisual)
		{
			AxialCoord axial = new AxialCoord(tileInfo.Axial.Q, tileInfo.Axial.R);
			_serverTileStates[axial] = new BattleTileState(
				tileInfo.TileType,
				tileInfo.OverlayType,
				tileInfo.EquipmentKey,
				tileInfo.EquipmentOwnerPawnId);

			if (updateGroundVisual)
				SetGroundTile(axial, tileInfo.TileType);

			switch (tileInfo.OverlayType)
			{
				case Protocol.BattleTileOverlayType.Ice:
					SetCombatOverlayTile(axial, iceOverlayTile);
					break;
				case Protocol.BattleTileOverlayType.Fire:
					SetCombatOverlayTile(axial, fireOverlayTile);
					break;
				case Protocol.BattleTileOverlayType.None:
					ClearCombatOverlayTile(axial);
					break;
				default:
					Debug.LogWarning($"Unsupported battle tile overlay. axial={axial}, overlay={tileInfo.OverlayType}");
					ClearCombatOverlayTile(axial);
				break;
			}

			UpdateEquipmentMarker(axial, tileInfo.EquipmentKey);
		}

		void UpdateEquipmentMarker(AxialCoord axial, string equipmentKey)
		{
			bool isAxe = string.Equals(equipmentKey, "AXE", System.StringComparison.OrdinalIgnoreCase);
			bool isParvis = string.Equals(equipmentKey, "PARVIS", System.StringComparison.OrdinalIgnoreCase);
			if (isAxe == false && isParvis == false)
			{
				if (_equipmentMarkers.TryGetValue(axial, out TextMesh existing))
				{
					Destroy(existing.gameObject);
					_equipmentMarkers.Remove(axial);
				}

				return;
			}

			if (_equipmentMarkers.TryGetValue(axial, out TextMesh marker) == false || marker == null)
			{
				GameObject markerObject = new GameObject(isParvis ? "Equipment_Parvis" : "Equipment_Axe");
				markerObject.transform.SetParent(transform, false);
				CreateEquipmentMarkerPlate(markerObject.transform);
				marker = markerObject.AddComponent<TextMesh>();
				marker.anchor = TextAnchor.MiddleCenter;
				marker.alignment = TextAlignment.Center;
				marker.characterSize = 0.12f;
				marker.fontSize = 42;
				marker.fontStyle = FontStyle.Bold;
				GameRoot.ApplyWorldTextFont(marker);
				MeshRenderer renderer = marker.GetComponent<MeshRenderer>();
				if (renderer != null)
					renderer.sortingOrder = 19;

				_equipmentMarkers[axial] = marker;
			}

			marker.gameObject.name = isParvis ? "Equipment_Parvis" : "Equipment_Axe";
			marker.text = isParvis ? "PARVIS" : "AXE";
			marker.color = isParvis
				? new Color(0.52f, 0.80f, 1f, 1f)
				: new Color(1f, 0.78f, 0.28f, 1f);
			marker.transform.position = AxialToWorldCenter(axial, -0.06f) + Vector3.up * 0.12f;
			marker.gameObject.SetActive(true);
		}

		static void CreateEquipmentMarkerPlate(Transform parent)
		{
			GameObject plateObject = new GameObject("Plate");
			plateObject.transform.SetParent(parent, false);
			plateObject.transform.localScale = new Vector3(0.55f, 0.26f, 1f);
			SpriteRenderer plate = plateObject.AddComponent<SpriteRenderer>();
			plate.sprite = GetEquipmentMarkerPlateSprite();
			plate.color = new Color(0.12f, 0.075f, 0.025f, 0.9f);
			plate.sortingOrder = 18;
		}

		static Sprite GetEquipmentMarkerPlateSprite()
		{
			if (_equipmentMarkerPlateSprite != null)
				return _equipmentMarkerPlateSprite;

			Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
			{
				name = "Runtime_EquipmentMarkerPlate",
				filterMode = FilterMode.Point,
				wrapMode = TextureWrapMode.Clamp,
				hideFlags = HideFlags.DontSave,
			};
			texture.SetPixel(0, 0, Color.white);
			texture.Apply();
			_equipmentMarkerPlateSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
			_equipmentMarkerPlateSprite.name = "Runtime_EquipmentMarkerPlate";
			return _equipmentMarkerPlateSprite;
		}

		void ClearEquipmentMarkers()
		{
			foreach (TextMesh marker in _equipmentMarkers.Values)
			{
				if (marker != null)
					Destroy(marker.gameObject);
			}

			_equipmentMarkers.Clear();
		}

		void SetGroundTile(AxialCoord axial, Protocol.BattleTileType tileType)
		{
			if (groundTilemap == null)
				return;

			TileBase tile = null;
			switch (tileType)
			{
				case Protocol.BattleTileType.Normal:
					tile = normalGroundTile;
					break;
				case Protocol.BattleTileType.Water:
					tile = waterGroundTile;
					break;
			}

			groundTilemap.SetTile(AxialToCell(axial), tile);
		}

		void ResolveTileAssetsFromAuthoredGround()
		{
			if (groundTilemap == null || (normalGroundTile != null && waterGroundTile != null && iceOverlayTile != null && fireOverlayTile != null))
				return;

			TileBase[] authoredTiles = groundTilemap.GetTilesBlock(groundTilemap.cellBounds);
			for (int i = 0; i < authoredTiles.Length; i++)
			{
				TileBase tile = authoredTiles[i];
				if (tile == null)
					continue;

				switch (tile.name)
				{
					case "Grass":
						normalGroundTile ??= tile;
						break;
					case "Water":
						waterGroundTile ??= tile;
						break;
					case "Ice":
						iceOverlayTile ??= tile;
						break;
					case "Fire":
						fireOverlayTile ??= tile;
						break;
				}
			}
		}

		async Task<TileBase> LoadTileAsync(string address)
		{
			AsyncOperationHandle<TileBase> handle = Addressables.LoadAssetAsync<TileBase>(address);
			_loadedTileHandles.Add(handle);
			await handle.Task;
			if (handle.Status == AsyncOperationStatus.Succeeded)
				return handle.Result;

			Debug.LogError($"Failed to load battle tile addressable: {address}");
			return null;
		}

		public TileBase GetCombatOverlayTile(AxialCoord axial)
		{
			return combatOverlayTilemap != null
				? combatOverlayTilemap.GetTile(AxialToCell(axial))
				: null;
		}

		public void SetCombatOverlayTile(AxialCoord axial, TileBase tile)
		{
			if (combatOverlayTilemap == null)
			{
				Debug.LogWarning($"{nameof(BattleMapGrid)} is missing CombatOverlay_Tilemap.");
				return;
			}

			combatOverlayTilemap.SetTile(AxialToCell(axial), tile);
		}

		public void ClearCombatOverlayTile(AxialCoord axial)
		{
			SetCombatOverlayTile(axial, null);
		}

		readonly struct BattleTileState
		{
			public readonly Protocol.BattleTileType TileType;
			public readonly Protocol.BattleTileOverlayType OverlayType;
			public readonly string EquipmentKey;
			public readonly ulong EquipmentOwnerPawnId;

			public BattleTileState(Protocol.BattleTileType tileType, Protocol.BattleTileOverlayType overlayType, string equipmentKey, ulong equipmentOwnerPawnId)
			{
				TileType = tileType;
				OverlayType = overlayType;
				EquipmentKey = equipmentKey ?? string.Empty;
				EquipmentOwnerPawnId = equipmentOwnerPawnId;
			}
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
