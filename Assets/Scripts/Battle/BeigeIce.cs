using System.Collections.Generic;
using UnityEngine;

namespace Battle
{
	[DisallowMultipleComponent]
	public sealed class BeigeIce : Beige
	{
		const string StormCenterAuraSkillKey = "BEIGE_ICE_STORM_CENTER";

		BeigeIceStormCenterAuraVisual _stormCenterAuraVisual;

		protected override void OnPawnInitialized()
		{
			RefreshStormCenterAura();
		}

		protected override void OnPawnStateChanged()
		{
			RefreshStormCenterAura();
		}

		protected override void OnAxialChanged()
		{
			RefreshStormCenterAura();
		}

		protected override void OnPawnDisabled()
		{
			if (_stormCenterAuraVisual != null)
				_stormCenterAuraVisual.Hide();
		}

		void RefreshStormCenterAura()
		{
			if (Auras.TryGetValue(StormCenterAuraSkillKey, out AuraState aura)
				&& IsDead == false
				&& MapGrid != null)
			{
				if (_stormCenterAuraVisual == null)
					_stormCenterAuraVisual = GetComponent<BeigeIceStormCenterAuraVisual>()
						?? gameObject.AddComponent<BeigeIceStormCenterAuraVisual>();

				_stormCenterAuraVisual.Show(MapGrid, Axial, aura.Radius);
				return;
			}

			if (_stormCenterAuraVisual != null)
				_stormCenterAuraVisual.Hide();
		}
	}

	// Presentation-only Beige Ice effect. Damage and range judgement remain server-authoritative.
	[DisallowMultipleComponent]
	sealed class BeigeIceStormCenterAuraVisual : MonoBehaviour
	{
		const int SortingOrder = 19;
		const float PulseSpeed = 2.4f;
		const float MinAlpha = 0.38f;
		const float MaxAlpha = 0.78f;

		LineRenderer _ring;
		Material _material;
		BattleMapGrid _mapGrid;
		AxialCoord _axial;
		int _radius;
		bool _visible;

		public void Show(BattleMapGrid mapGrid, AxialCoord axial, int radius)
		{
			if (mapGrid == null || radius <= 0)
			{
				Hide();
				return;
			}

			_mapGrid = mapGrid;
			_axial = axial;
			_radius = radius;
			EnsureRing();
			UpdateRingPositions();
			_visible = true;
			_ring.enabled = true;
		}

		public void Hide()
		{
			_visible = false;
			if (_ring != null)
				_ring.enabled = false;
		}

		void LateUpdate()
		{
			if (_visible == false || _ring == null || _ring.enabled == false)
				return;

			UpdateRingPositions();
			float pulse = (Mathf.Sin(Time.time * PulseSpeed) + 1f) * 0.5f;
			Color color = new Color(0.36f, 0.86f, 1f, Mathf.Lerp(MinAlpha, MaxAlpha, pulse));
			_ring.startColor = color;
			_ring.endColor = color;
		}

		void OnDestroy()
		{
			if (_material != null)
				Destroy(_material);
		}

		void EnsureRing()
		{
			if (_ring != null)
				return;

			_ring = gameObject.AddComponent<LineRenderer>();
			_ring.useWorldSpace = true;
			_ring.loop = true;
			_ring.positionCount = 6;
			_ring.startWidth = 0.055f;
			_ring.endWidth = 0.055f;
			_ring.numCornerVertices = 2;
			_ring.numCapVertices = 2;
			_ring.sortingOrder = SortingOrder;
			Shader shader = Shader.Find("Sprites/Default");
			if (shader != null)
			{
				_material = new Material(shader);
				_ring.material = _material;
			}
		}

		void UpdateRingPositions()
		{
			if (_ring == null || _mapGrid == null)
				return;

			Vector3 center = _mapGrid.AxialToWorldCenter(_axial, transform.position.z);
			List<Vector3> perimeter = new List<Vector3>(6);
			for (int direction = 0; direction < 6; direction++)
			{
				AxialCoord edge = _axial;
				for (int step = 0; step < _radius; step++)
					edge = _mapGrid.GetNeighbor(edge, direction);

				perimeter.Add(_mapGrid.AxialToWorldCenter(edge, transform.position.z));
			}

			perimeter.Sort((left, right) =>
				Mathf.Atan2(left.y - center.y, left.x - center.x)
					.CompareTo(Mathf.Atan2(right.y - center.y, right.x - center.x)));

			for (int index = 0; index < perimeter.Count; index++)
				_ring.SetPosition(index, perimeter[index]);
		}
	}
}
