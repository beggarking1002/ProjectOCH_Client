using System.Collections.Generic;
using UnityEngine;

namespace Field
{
	[DisallowMultipleComponent]
	public sealed class FieldMapWalkArea : MonoBehaviour
	{
		readonly HashSet<Vector2Int> _walkableCells = new HashSet<Vector2Int>();

		FieldWalkMapData _data;

		public Transform PlaneTransform => transform;
		public bool IsInitialized => _data != null && _walkableCells.Count > 0;
		public string MapId => _data?.map_id;

		public bool Initialize(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
			{
				Debug.LogError($"{nameof(FieldMapWalkArea)} received an empty walk map JSON.");
				return false;
			}

			FieldWalkMapData data = JsonUtility.FromJson<FieldWalkMapData>(json);
			return Initialize(data);
		}

		public bool Initialize(FieldWalkMapData data)
		{
			_walkableCells.Clear();
			_data = null;

			if (data == null || data.fixed_point_scale <= 0 || data.cell_size.x <= 0f || data.cell_size.y <= 0f)
			{
				Debug.LogError($"{nameof(FieldMapWalkArea)} received invalid walk map metadata.");
				return false;
			}

			if (data.walkable_ranges != null)
			{
				for (int i = 0; i < data.walkable_ranges.Count; i++)
				{
					FieldWalkMapRange range = data.walkable_ranges[i];
					if (range.x_min > range.x_max)
						continue;

					for (int x = range.x_min; x <= range.x_max; x++)
						_walkableCells.Add(new Vector2Int(x, range.y));
				}
			}

			// Village areas are interaction targets, not traversable terrain. Remove
			// them after expanding walkable_ranges so local input and pathfinding match
			// the server's authoritative movement rules.
			if (data.village_areas != null)
			{
				for (int areaIndex = 0; areaIndex < data.village_areas.Count; areaIndex++)
				{
					FieldVillageArea area = data.village_areas[areaIndex];
					if (area?.tile_ranges == null)
						continue;

					for (int rangeIndex = 0; rangeIndex < area.tile_ranges.Count; rangeIndex++)
					{
						FieldWalkMapRange range = area.tile_ranges[rangeIndex];
						for (int x = range.x_min; x <= range.x_max; x++)
							_walkableCells.Remove(new Vector2Int(x, range.y));
					}
				}
			}

			if (_walkableCells.Count == 0)
			{
				Debug.LogError($"{nameof(FieldMapWalkArea)} map '{data.map_id}' has no walkable cells.");
				return false;
			}

			_data = data;
			return true;
		}

		public Vector3 GetDefaultSpawnPosition(float z)
		{
			EnsureInitialized();

			Vector2Int spawnCell = new Vector2Int(0, 0);
			if (_walkableCells.Contains(spawnCell) == false || TryGetVillageAtCell(spawnCell, out _))
				spawnCell = GetNearestWalkableCell(Vector2Int.zero, includeVillageCells: false);

			return CellToWorld(spawnCell, z);
		}

		public bool IsWalkable(Vector3 worldPosition)
		{
			if (IsInitialized == false)
				return false;

			return _walkableCells.Contains(WorldToNearestCell(worldPosition));
		}

		public bool TryGetCell(Vector3 worldPosition, out Vector2Int cell)
		{
			cell = default;
			if (IsInitialized == false)
				return false;

			cell = WorldToNearestCell(worldPosition);
			return true;
		}

		public Vector3 GetCellCenterWorld(Vector2Int cell, float z)
		{
			EnsureInitialized();
			return CellToWorld(cell, z);
		}

		public static int GetHexDistance(Vector2Int from, Vector2Int to)
		{
			int fromQ = from.x - (from.y - (from.y & 1)) / 2;
			int toQ = to.x - (to.y - (to.y & 1)) / 2;
			int deltaQ = toQ - fromQ;
			int deltaR = to.y - from.y;
			return Mathf.Max(Mathf.Abs(deltaQ), Mathf.Abs(deltaR), Mathf.Abs(deltaQ + deltaR));
		}

		// This is visual/input filtering only. The server validates village entry.
		public bool TryGetVillageAt(Vector3 worldPosition, out Vector2Int cell, out string villageId)
		{
			cell = default;
			villageId = null;
			if (IsInitialized == false || _data.village_areas == null)
				return false;

			cell = WorldToNearestCell(worldPosition);
			return TryGetVillageAtCell(cell, out villageId);
		}

		bool TryGetVillageAtCell(Vector2Int cell, out string villageId)
		{
			villageId = null;
			if (_data == null || _data.village_areas == null)
				return false;

			for (int areaIndex = 0; areaIndex < _data.village_areas.Count; areaIndex++)
			{
				FieldVillageArea area = _data.village_areas[areaIndex];
				if (area == null || string.IsNullOrWhiteSpace(area.village_id) || area.tile_ranges == null)
					continue;

				for (int rangeIndex = 0; rangeIndex < area.tile_ranges.Count; rangeIndex++)
				{
					FieldWalkMapRange range = area.tile_ranges[rangeIndex];
					if (cell.y != range.y || cell.x < range.x_min || cell.x > range.x_max)
						continue;

					villageId = area.village_id;
					return true;
				}
			}

			return false;
		}

		// Finds a reachable non-village tile from which the server allows entry to
		// the selected village cell (within its two-hex interaction range).
		public bool TryGetVillageApproachCell(Vector3 startWorldPosition, Vector2Int villageCell, out Vector2Int approachCell)
		{
			approachCell = default;
			if (IsInitialized == false)
				return false;

			bool found = false;
			float bestPathLength = float.MaxValue;
			List<Vector3> candidatePath = new List<Vector3>();
			foreach (Vector2Int candidateCell in _walkableCells)
			{
				if (GetHexDistance(candidateCell, villageCell) > 2)
					continue;

				candidatePath.Clear();
				Vector3 candidateWorldPosition = CellToWorld(candidateCell, startWorldPosition.z);
				if (TryFindPath(startWorldPosition, candidateWorldPosition, candidatePath) == false)
					continue;

				float pathLength = GetPathLength(startWorldPosition, candidatePath);
				if (found && (pathLength > bestPathLength ||
					(Mathf.Approximately(pathLength, bestPathLength) && CompareCell(candidateCell, approachCell) >= 0)))
				{
					continue;
				}

				found = true;
				bestPathLength = pathLength;
				approachCell = candidateCell;
			}

			return found;
		}

		// The server remains authoritative for the destination. This local route is
		// only used to immediately replace an in-flight visual route when a newer
		// S_MOVE arrives, so a pawn never has to finish its previous path first.
		public bool TryFindPath(Vector3 startWorldPosition, Vector3 targetWorldPosition, List<Vector3> outPath)
		{
			outPath?.Clear();
			if (outPath == null || IsInitialized == false)
				return false;

			Vector2Int start = WorldToNearestCell(startWorldPosition);
			Vector2Int target = WorldToNearestCell(targetWorldPosition);
			if (_walkableCells.Contains(start) == false || _walkableCells.Contains(target) == false)
				return false;
			if (start == target)
			{
				outPath.Add(targetWorldPosition);
				return true;
			}

			Queue<Vector2Int> open = new Queue<Vector2Int>();
			Dictionary<Vector2Int, Vector2Int> predecessors = new Dictionary<Vector2Int, Vector2Int>();
			HashSet<Vector2Int> visited = new HashSet<Vector2Int> { start };
			open.Enqueue(start);
			while (open.Count > 0 && visited.Contains(target) == false)
			{
				Vector2Int current = open.Dequeue();
				foreach (Vector2Int neighbor in GetNeighbors(current))
				{
					if (_walkableCells.Contains(neighbor) == false || visited.Add(neighbor) == false)
						continue;

					predecessors[neighbor] = current;
					open.Enqueue(neighbor);
					if (neighbor == target)
						break;
				}
			}

			if (visited.Contains(target) == false)
				return false;

			List<Vector3> rawPath = new List<Vector3>();
			for (Vector2Int current = target; current != start; current = predecessors[current])
				rawPath.Add(CellToWorld(current, startWorldPosition.z));
			rawPath.Reverse();
			if (rawPath.Count > 0)
				rawPath.RemoveAt(rawPath.Count - 1);
			rawPath.Add(targetWorldPosition);

			Vector3 routeStart = startWorldPosition;
			for (int currentIndex = 0; currentIndex < rawPath.Count;)
			{
				int nextIndex = currentIndex;
				for (int candidateIndex = rawPath.Count - 1; candidateIndex >= currentIndex; candidateIndex--)
				{
					if (HasWalkableLineOfSight(routeStart, rawPath[candidateIndex]))
					{
						nextIndex = candidateIndex;
						break;
					}
				}

				outPath.Add(rawPath[nextIndex]);
				routeStart = rawPath[nextIndex];
				currentIndex = nextIndex + 1;
			}

			return true;
		}

		IEnumerable<Vector2Int> GetNeighbors(Vector2Int cell)
		{
			int diagonalX = (cell.y & 1) == 0 ? cell.x - 1 : cell.x + 1;
			yield return new Vector2Int(cell.x - 1, cell.y);
			yield return new Vector2Int(cell.x + 1, cell.y);
			yield return new Vector2Int(cell.x, cell.y - 1);
			yield return new Vector2Int(diagonalX, cell.y - 1);
			yield return new Vector2Int(cell.x, cell.y + 1);
			yield return new Vector2Int(diagonalX, cell.y + 1);
		}

		bool HasWalkableLineOfSight(Vector3 from, Vector3 to)
		{
			int sampleCount = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(from, to) * 10f));
			for (int sampleIndex = 0; sampleIndex <= sampleCount; sampleIndex++)
			{
				Vector3 sample = Vector3.Lerp(from, to, sampleIndex / (float)sampleCount);
				if (IsWalkable(sample) == false)
					return false;
			}

			return true;
		}

		static float GetPathLength(Vector3 start, List<Vector3> path)
		{
			float length = 0f;
			Vector3 previous = start;
			for (int index = 0; index < path.Count; index++)
			{
				length += Vector3.Distance(previous, path[index]);
				previous = path[index];
			}
			return length;
		}

		Vector3 CellToWorld(Vector2Int cell, float z)
		{
			float x = _data.origin_world.x + (cell.x + GetOddRowOffset(cell.y)) * _data.cell_size.x;
			float y = _data.origin_world.y + cell.y * _data.cell_size.y * 0.75f;
			return new Vector3(x, y, z);
		}

		Vector2Int WorldToNearestCell(Vector3 worldPosition)
		{
			float rowStep = _data.cell_size.y * 0.75f;
			int estimatedRow = Mathf.RoundToInt((worldPosition.y - _data.origin_world.y) / rowStep);
			int estimatedColumn = Mathf.RoundToInt(
				(worldPosition.x - _data.origin_world.x) / _data.cell_size.x - GetOddRowOffset(estimatedRow));

			Vector2Int nearestCell = new Vector2Int(estimatedColumn, estimatedRow);
			float nearestDistance = float.MaxValue;
			for (int y = estimatedRow - 1; y <= estimatedRow + 1; y++)
			{
				for (int x = estimatedColumn - 1; x <= estimatedColumn + 1; x++)
				{
					Vector2Int candidate = new Vector2Int(x, y);
					float distance = (CellToWorld(candidate, worldPosition.z) - worldPosition).sqrMagnitude;
					if (distance >= nearestDistance)
						continue;

					nearestDistance = distance;
					nearestCell = candidate;
				}
			}

			return nearestCell;
		}

		Vector2Int GetNearestWalkableCell(Vector2Int origin, bool includeVillageCells)
		{
			Vector2Int result = default;
			int nearestDistance = int.MaxValue;
			foreach (Vector2Int candidate in _walkableCells)
			{
				if (includeVillageCells == false && TryGetVillageAtCell(candidate, out _))
					continue;

				int distance = Mathf.Abs(candidate.x - origin.x) + Mathf.Abs(candidate.y - origin.y);
				if (distance > nearestDistance || (distance == nearestDistance && CompareCell(candidate, result) >= 0))
					continue;

				nearestDistance = distance;
				result = candidate;
			}

			return result;
		}

		static int CompareCell(Vector2Int left, Vector2Int right)
		{
			int byY = left.y.CompareTo(right.y);
			return byY != 0 ? byY : left.x.CompareTo(right.x);
		}

		static float GetOddRowOffset(int row)
		{
			return (row & 1) == 0 ? 0f : 0.5f;
		}

		void EnsureInitialized()
		{
			if (IsInitialized == false)
				throw new MissingReferenceException($"{nameof(FieldMapWalkArea)} requires a loaded walk map JSON.");
		}
	}
}
