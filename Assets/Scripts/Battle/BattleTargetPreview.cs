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
		static readonly Color ValidTargetColor = new Color(0.25f, 1f, 0.48f, 0.65f);
		static readonly Color AffectedTileColor = new Color(1f, 0.64f, 0.18f, 0.95f);

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
