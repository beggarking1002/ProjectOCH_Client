using System;
using System.Collections.Generic;

namespace Field
{
	[Serializable]
	public sealed class FieldWalkMapData
	{
		public string map_id;
		public int fixed_point_scale;
		public FieldWalkMapVector2 cell_size;
		public FieldWalkMapVector2 origin_world;
		public FieldWalkMapBounds bounds;
		public List<FieldWalkMapRange> walkable_ranges = new List<FieldWalkMapRange>();
		public List<FieldWalkMapRange> water_ranges = new List<FieldWalkMapRange>();
		public List<FieldVillageArea> village_areas = new List<FieldVillageArea>();
		public List<FieldWalkMapCell> debug_walkable_cells = new List<FieldWalkMapCell>();
	}

	[Serializable]
	public struct FieldWalkMapVector2
	{
		public float x;
		public float y;

		public FieldWalkMapVector2(float x, float y)
		{
			this.x = x;
			this.y = y;
		}
	}

	[Serializable]
	public struct FieldWalkMapBounds
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

	[Serializable]
	public struct FieldWalkMapCell
	{
		public int cell_x;
		public int cell_y;
		public int world_x;
		public int world_y;
	}

	[Serializable]
	public struct FieldWalkMapRange
	{
		public int y;
		public int x_min;
		public int x_max;
	}

	[Serializable]
	public sealed class FieldVillageArea
	{
		public string village_id;
		public List<FieldWalkMapRange> tile_ranges = new List<FieldWalkMapRange>();
	}
}
