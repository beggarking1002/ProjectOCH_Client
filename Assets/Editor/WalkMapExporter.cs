using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

internal sealed class WalkMapExporter : EditorWindow
{
	const string DefaultClientOutputDirectory = "Assets/GameData/Maps";
	const string DefaultServerOutputDirectory = @"C:\ProjectOCH\Server\Data\Maps";
	const int FixedPointScale = 100;

	[SerializeField] string mapId;
	[SerializeField] GameObject mapPrefab;
	[SerializeField] Tilemap groundTilemap;
	[SerializeField] Tilemap blockTilemap;
	[SerializeField] bool subtractBlockTilemap;
	[SerializeField] string clientOutputDirectory = DefaultClientOutputDirectory;
	[SerializeField] bool copyToServerDataDirectory;
	[SerializeField] string serverOutputDirectory = DefaultServerOutputDirectory;
	[SerializeField] bool includeDebugCells;

	[MenuItem("Tools/Project OCH/Map/Walk Map Exporter")]
	static void Open()
	{
		WalkMapExporter window = GetWindow<WalkMapExporter>("Walk Map Exporter");
		window.minSize = new Vector2(420f, 300f);
		window.InitializeDefaults();
		window.Show();
	}

	void OnGUI()
	{
		InitializeDefaults();

		EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
		mapId = EditorGUILayout.TextField("Map Id", mapId);
		EditorGUI.BeginChangeCheck();
		mapPrefab = (GameObject)EditorGUILayout.ObjectField("Map Prefab", mapPrefab, typeof(GameObject), false);
		if (EditorGUI.EndChangeCheck() && mapPrefab != null)
		{
			mapId = mapPrefab.name;
			groundTilemap = null;
			blockTilemap = null;
		}

		groundTilemap = (Tilemap)EditorGUILayout.ObjectField("Ground Tilemap", groundTilemap, typeof(Tilemap), true);
		blockTilemap = (Tilemap)EditorGUILayout.ObjectField("Block Tilemap", blockTilemap, typeof(Tilemap), true);
		subtractBlockTilemap = EditorGUILayout.Toggle("Subtract Block Tilemap", subtractBlockTilemap);

		using (new EditorGUILayout.HorizontalScope())
		{
			if (GUILayout.Button("Auto Fill From Selection"))
				AutoFillFromSelection();

			if (GUILayout.Button("Auto Fill From Scene"))
				AutoFillFromScene();
		}

		EditorGUILayout.Space(8f);
		EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
		clientOutputDirectory = EditorGUILayout.TextField("Client Output", clientOutputDirectory);
		includeDebugCells = EditorGUILayout.Toggle("Include Debug Cells", includeDebugCells);
		copyToServerDataDirectory = EditorGUILayout.Toggle("Copy To Server", copyToServerDataDirectory);

		using (new EditorGUI.DisabledScope(copyToServerDataDirectory == false))
			serverOutputDirectory = EditorGUILayout.TextField("Server Output", serverOutputDirectory);

		EditorGUILayout.Space(8f);
		using (new EditorGUI.DisabledScope((mapPrefab == null && groundTilemap == null) || string.IsNullOrWhiteSpace(mapId)))
		{
			if (GUILayout.Button("Export Walk Map JSON", GUILayout.Height(32f)))
				Export();
		}

		EditorGUILayout.HelpBox(
			"Export rule: Ground Tilemap cells are walkable. If Subtract Block Tilemap is enabled, cells that also exist in Block/Prop Tilemap are excluded. Output is compressed by row ranges.",
			MessageType.Info);
	}

	void InitializeDefaults()
	{
		if (string.IsNullOrWhiteSpace(mapId))
		{
			string sceneName = SceneManager.GetActiveScene().name;
			mapId = string.IsNullOrWhiteSpace(sceneName) ? "field_001" : sceneName;
		}

		if (string.IsNullOrWhiteSpace(clientOutputDirectory))
			clientOutputDirectory = DefaultClientOutputDirectory;

		if (string.IsNullOrWhiteSpace(serverOutputDirectory))
			serverOutputDirectory = DefaultServerOutputDirectory;
	}

	void AutoFillFromSelection()
	{
		GameObject selected = Selection.activeObject as GameObject;
		if (selected == null)
			return;

		if (PrefabUtility.IsPartOfPrefabAsset(selected))
		{
			mapPrefab = selected;
			mapId = selected.name;
			groundTilemap = null;
			blockTilemap = null;
			return;
		}

		mapPrefab = null;
		FillTilemapsFromRoot(selected);
	}

	void AutoFillFromScene()
	{
		Grid grid = FindFirstObjectByType<Grid>();
		if (grid != null)
		{
			mapPrefab = null;
			FillTilemapsFromRoot(grid.gameObject);
		}
	}

	void FillTilemapsFromRoot(GameObject root)
	{
		if (root == null)
			return;

		Tilemap[] tilemaps = root.GetComponentsInChildren<Tilemap>(true);
		for (int i = 0; i < tilemaps.Length; i++)
		{
			Tilemap tilemap = tilemaps[i];
			if (tilemap.name == "Ground_Tilemap")
				groundTilemap = tilemap;
			else if (IsBlockTilemapName(tilemap.name))
				blockTilemap = tilemap;
		}
	}

	void Export()
	{
		ExportWalkMapData data = BuildDataFromCurrentSource();
		string json = JsonUtility.ToJson(data, true);
		string fileName = $"{mapId}.walkmap.json";

		string clientPath = WriteJson(clientOutputDirectory, fileName, json);
		AssetDatabase.ImportAsset(clientPath.Replace('\\', '/'));

		if (copyToServerDataDirectory)
			WriteJson(serverOutputDirectory, fileName, json);

		Debug.Log($"Exported walk map: {clientPath} ({data.walkable_ranges.Count} ranges)");
	}

	ExportWalkMapData BuildDataFromCurrentSource()
	{
		if (mapPrefab == null)
		{
			if (groundTilemap == null)
				throw new MissingReferenceException("Ground Tilemap is required.");

			return BuildData(groundTilemap, subtractBlockTilemap ? blockTilemap : null);
		}

		string prefabPath = AssetDatabase.GetAssetPath(mapPrefab);
		if (string.IsNullOrWhiteSpace(prefabPath))
			throw new MissingReferenceException("Map Prefab must be a prefab asset.");

		GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
		try
		{
			Tilemap prefabGround = null;
			Tilemap prefabBlock = null;
			FindTilemaps(prefabRoot, ref prefabGround, ref prefabBlock);

			if (prefabGround == null)
				throw new MissingReferenceException($"Ground_Tilemap not found in prefab: {prefabPath}");

			return BuildData(prefabGround, subtractBlockTilemap ? prefabBlock : null);
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents(prefabRoot);
		}
	}

	ExportWalkMapData BuildData(Tilemap sourceGroundTilemap, Tilemap sourceBlockTilemap)
	{
		BoundsInt bounds = sourceGroundTilemap.cellBounds;
		ExportWalkMapData data = new ExportWalkMapData
		{
			map_id = mapId,
			fixed_point_scale = FixedPointScale,
			cell_size = new ExportVector2(sourceGroundTilemap.layoutGrid.cellSize.x, sourceGroundTilemap.layoutGrid.cellSize.y),
			origin_world = new ExportVector2(sourceGroundTilemap.transform.position.x, sourceGroundTilemap.transform.position.y),
			bounds = new ExportBounds
			{
				min_cell_x = int.MaxValue,
				min_cell_y = int.MaxValue,
				max_cell_x = int.MinValue,
				max_cell_y = int.MinValue,
				min_world_x = int.MaxValue,
				min_world_y = int.MaxValue,
				max_world_x = int.MinValue,
				max_world_y = int.MinValue,
			}
		};

		for (int y = bounds.yMin; y < bounds.yMax; y++)
		{
			int rangeStart = int.MinValue;
			int rangeEnd = int.MinValue;

			for (int x = bounds.xMin; x < bounds.xMax; x++)
			{
				Vector3Int cell = new Vector3Int(x, y, 0);
				bool walkable = IsWalkable(sourceGroundTilemap, sourceBlockTilemap, cell);
				if (walkable)
				{
					if (rangeStart == int.MinValue)
						rangeStart = x;

					rangeEnd = x;
					ExpandBounds(ref data.bounds, cell, sourceGroundTilemap);

					if (includeDebugCells)
						data.debug_walkable_cells.Add(CreateDebugCell(cell, sourceGroundTilemap));

					continue;
				}

				FlushRange(data, y, ref rangeStart, ref rangeEnd);
			}

			FlushRange(data, y, ref rangeStart, ref rangeEnd);
		}

		if (data.walkable_ranges.Count == 0)
			data.bounds = default;

		return data;
	}

	static bool IsWalkable(Tilemap sourceGroundTilemap, Tilemap sourceBlockTilemap, Vector3Int cell)
	{
		if (sourceGroundTilemap == null || sourceGroundTilemap.HasTile(cell) == false)
			return false;

		return sourceBlockTilemap == null || sourceBlockTilemap.HasTile(cell) == false;
	}

	static void FindTilemaps(GameObject root, ref Tilemap foundGround, ref Tilemap foundProp)
	{
		if (root == null)
			return;

		Tilemap[] tilemaps = root.GetComponentsInChildren<Tilemap>(true);
		for (int i = 0; i < tilemaps.Length; i++)
		{
			Tilemap tilemap = tilemaps[i];
			if (tilemap.name == "Ground_Tilemap")
				foundGround = tilemap;
			else if (IsBlockTilemapName(tilemap.name))
				foundProp = tilemap;
		}
	}

	static bool IsBlockTilemapName(string tilemapName)
	{
		return tilemapName == "Block_Tilemap" || tilemapName == "Prop_Tilemap";
	}

	static void FlushRange(ExportWalkMapData data, int y, ref int rangeStart, ref int rangeEnd)
	{
		if (rangeStart == int.MinValue)
			return;

		data.walkable_ranges.Add(new ExportRange
		{
			y = y,
			x_min = rangeStart,
			x_max = rangeEnd,
		});

		rangeStart = int.MinValue;
		rangeEnd = int.MinValue;
	}

	static ExportCell CreateDebugCell(Vector3Int cell, Tilemap sourceGroundTilemap)
	{
		Vector3 world = sourceGroundTilemap.GetCellCenterWorld(cell);
		return new ExportCell
		{
			cell_x = cell.x,
			cell_y = cell.y,
			world_x = Mathf.RoundToInt(world.x * FixedPointScale),
			world_y = Mathf.RoundToInt(world.y * FixedPointScale),
		};
	}

	static void ExpandBounds(ref ExportBounds bounds, Vector3Int cell, Tilemap sourceGroundTilemap)
	{
		Vector3 world = sourceGroundTilemap.GetCellCenterWorld(cell);
		int worldX = Mathf.RoundToInt(world.x * FixedPointScale);
		int worldY = Mathf.RoundToInt(world.y * FixedPointScale);

		bounds.min_cell_x = Mathf.Min(bounds.min_cell_x, cell.x);
		bounds.min_cell_y = Mathf.Min(bounds.min_cell_y, cell.y);
		bounds.max_cell_x = Mathf.Max(bounds.max_cell_x, cell.x);
		bounds.max_cell_y = Mathf.Max(bounds.max_cell_y, cell.y);
		bounds.min_world_x = Mathf.Min(bounds.min_world_x, worldX);
		bounds.min_world_y = Mathf.Min(bounds.min_world_y, worldY);
		bounds.max_world_x = Mathf.Max(bounds.max_world_x, worldX);
		bounds.max_world_y = Mathf.Max(bounds.max_world_y, worldY);
	}

	static string WriteJson(string directory, string fileName, string json)
	{
		Directory.CreateDirectory(directory);
		string path = Path.Combine(directory, fileName);
		File.WriteAllText(path, json, Encoding.UTF8);
		return path;
	}

	[System.Serializable]
	sealed class ExportWalkMapData
	{
		public string map_id;
		public int fixed_point_scale;
		public ExportVector2 cell_size;
		public ExportVector2 origin_world;
		public ExportBounds bounds;
		public System.Collections.Generic.List<ExportRange> walkable_ranges = new System.Collections.Generic.List<ExportRange>();
		public System.Collections.Generic.List<ExportCell> debug_walkable_cells = new System.Collections.Generic.List<ExportCell>();
	}

	[System.Serializable]
	struct ExportVector2
	{
		public float x;
		public float y;

		public ExportVector2(float x, float y)
		{
			this.x = x;
			this.y = y;
		}
	}

	[System.Serializable]
	struct ExportBounds
	{
		public int min_cell_x;
		public int min_cell_y;
		public int max_cell_x;
		public int max_cell_y;
		public int min_world_x;
		public int min_world_y;
		public int max_world_x;
		public int max_world_y;
	}

	[System.Serializable]
	struct ExportCell
	{
		public int cell_x;
		public int cell_y;
		public int world_x;
		public int world_y;
	}

	[System.Serializable]
	struct ExportRange
	{
		public int y;
		public int x_min;
		public int x_max;
	}
}
