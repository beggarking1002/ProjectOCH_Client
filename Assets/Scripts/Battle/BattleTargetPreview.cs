using System.Collections.Generic;
using UnityEngine;

namespace Battle
{
	// Client-side targeting aid only. Server-side validation remains authoritative.
	[DisallowMultipleComponent]
	public sealed class BattleTargetPreview : MonoBehaviour
	{
		// Keep tile targeting above the map but below pawn rings (19) and pawn sprites (20).
		const int SortingOrder = 18;
		static readonly Color SkillRangeColor = new Color(0.35f, 0.72f, 1f, 0.32f);
		static readonly Color ValidTargetColor = new Color(0.25f, 1f, 0.48f, 0.65f);
		static readonly Color AffectedTileColor = new Color(1f, 0.64f, 0.18f, 0.95f);
		static readonly Color ZocTileColor = new Color(1f, 0.8f, 0.18f, 0.7f);
		static readonly Color ZocAttackerColor = new Color(1f, 0.22f, 0.2f, 0.98f);

		readonly List<LineRenderer> _outlines = new List<LineRenderer>();
		Material _material;

		public void Show(BattleMapGrid mapGrid, IReadOnlyList<AxialCoord> validTargets, IReadOnlyList<AxialCoord> affectedTiles)
		{
			if (mapGrid == null)
			{
				Hide();
				return;
			}

			int required = (validTargets?.Count ?? 0) + (affectedTiles?.Count ?? 0);
			EnsureOutlineCount(required);
			int index = 0;
			if (validTargets != null)
			{
				for (int i = 0; i < validTargets.Count; i++)
					DrawHex(_outlines[index++], mapGrid, validTargets[i], ValidTargetColor, 0.72f);
			}

			if (affectedTiles != null)
			{
				for (int i = 0; i < affectedTiles.Count; i++)
					DrawHex(_outlines[index++], mapGrid, affectedTiles[i], AffectedTileColor, 1f);
			}

			for (; index < _outlines.Count; index++)
				_outlines[index].enabled = false;
		}

		// Skill range is intentionally drawn separately from valid targets. A spell can
		// reach an empty tile but still require an enemy, ally, or FIRE overlay there.
		// Showing both lets players read the distance rule before a target exists.
		public void ShowSkillRange(
			BattleMapGrid mapGrid,
			IReadOnlyList<AxialCoord> rangeTiles,
			IReadOnlyList<AxialCoord> validTargets,
			IReadOnlyList<AxialCoord> affectedTiles)
		{
			if (mapGrid == null)
			{
				Hide();
				return;
			}

			int required = (rangeTiles?.Count ?? 0)
				+ (validTargets?.Count ?? 0)
				+ (affectedTiles?.Count ?? 0);
			EnsureOutlineCount(required);
			int index = 0;
			DrawTiles(mapGrid, rangeTiles, SkillRangeColor, 0.64f, ref index);
			DrawTiles(mapGrid, validTargets, ValidTargetColor, 0.72f, ref index);
			DrawTiles(mapGrid, affectedTiles, AffectedTileColor, 1f, ref index);
			for (; index < _outlines.Count; index++)
				_outlines[index].enabled = false;
		}

		// Move previews additionally identify both the enemy ZOC footprint and the
		// pawns that would make the reaction attack for the hovered destination.
		public void ShowMoveWithZoc(
			BattleMapGrid mapGrid,
			IReadOnlyList<AxialCoord> validTargets,
			IReadOnlyList<AxialCoord> zocTiles,
			IReadOnlyList<AxialCoord> zocAttackerTiles)
		{
			if (mapGrid == null)
			{
				Hide();
				return;
			}

			int required = (validTargets?.Count ?? 0)
				+ (zocTiles?.Count ?? 0)
				+ (zocAttackerTiles?.Count ?? 0);
			EnsureOutlineCount(required);
			int index = 0;
			DrawTiles(mapGrid, validTargets, ValidTargetColor, 0.72f, ref index);
			DrawTiles(mapGrid, zocTiles, ZocTileColor, 0.88f, ref index);
			DrawTiles(mapGrid, zocAttackerTiles, ZocAttackerColor, 1f, ref index);
			for (; index < _outlines.Count; index++)
				_outlines[index].enabled = false;
		}

		public void Hide()
		{
			for (int i = 0; i < _outlines.Count; i++)
				_outlines[i].enabled = false;
		}

		void OnDestroy()
		{
			if (_material != null)
				Destroy(_material);
		}

		void EnsureOutlineCount(int count)
		{
			while (_outlines.Count < count)
			{
				GameObject outlineObject = new GameObject("TargetPreviewHex");
				outlineObject.transform.SetParent(transform, false);
				LineRenderer outline = outlineObject.AddComponent<LineRenderer>();
				outline.useWorldSpace = true;
				outline.loop = true;
				outline.positionCount = 6;
				outline.startWidth = 0.045f;
				outline.endWidth = 0.045f;
				outline.numCornerVertices = 2;
				outline.sortingOrder = SortingOrder;
				outline.material = GetMaterial();
				_outlines.Add(outline);
			}
		}

		Material GetMaterial()
		{
			if (_material != null)
				return _material;

			Shader shader = Shader.Find("Sprites/Default");
			if (shader != null)
				_material = new Material(shader);

			return _material;
		}

		void DrawTiles(BattleMapGrid mapGrid, IReadOnlyList<AxialCoord> tiles, Color color, float scale, ref int index)
		{
			if (tiles == null)
				return;

			for (int i = 0; i < tiles.Count; i++)
				DrawHex(_outlines[index++], mapGrid, tiles[i], color, scale);
		}

		static void DrawHex(LineRenderer outline, BattleMapGrid mapGrid, AxialCoord axial, Color color, float scale)
		{
			Vector3 center = mapGrid.AxialToWorldCenter(axial, -0.05f);
			Vector3 neighbor = mapGrid.AxialToWorldCenter(mapGrid.GetNeighbor(axial, 0), -0.05f);
			float radius = Vector3.Distance(center, neighbor) * 0.48f * scale;
			for (int corner = 0; corner < 6; corner++)
			{
				float angle = (90f + 60f * corner) * Mathf.Deg2Rad;
				outline.SetPosition(corner, center + new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius));
			}

			outline.startColor = color;
			outline.endColor = color;
			outline.enabled = true;
		}
	}
}
