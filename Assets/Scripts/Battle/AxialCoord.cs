using System;
using UnityEngine;

namespace Battle
{
	[Serializable]
	public readonly struct AxialCoord : IEquatable<AxialCoord>
	{
		static readonly AxialCoord[] Directions =
		{
			new AxialCoord(1, 0),
			new AxialCoord(1, -1),
			new AxialCoord(0, -1),
			new AxialCoord(-1, 0),
			new AxialCoord(-1, 1),
			new AxialCoord(0, 1),
		};

		[SerializeField] readonly int q;
		[SerializeField] readonly int r;

		public int Q => q;
		public int R => r;
		public int S => -q - r;

		public AxialCoord(int q, int r)
		{
			this.q = q;
			this.r = r;
		}

		public static AxialCoord FromCell(Vector3Int cell)
		{
			return new AxialCoord(cell.x, cell.y);
		}

		public Vector3Int ToCell(int z = 0)
		{
			return new Vector3Int(q, r, z);
		}

		public AxialCoord Neighbor(int direction)
		{
			int index = ((direction % Directions.Length) + Directions.Length) % Directions.Length;
			return this + Directions[index];
		}

		public int DistanceTo(AxialCoord other)
		{
			return (Mathf.Abs(q - other.q) + Mathf.Abs(r - other.r) + Mathf.Abs(S - other.S)) / 2;
		}

		public bool Equals(AxialCoord other)
		{
			return q == other.q && r == other.r;
		}

		public override bool Equals(object obj)
		{
			return obj is AxialCoord other && Equals(other);
		}

		public override int GetHashCode()
		{
			unchecked
			{
				return (q * 397) ^ r;
			}
		}

		public override string ToString()
		{
			return $"({q}, {r})";
		}

		public static AxialCoord operator +(AxialCoord a, AxialCoord b)
		{
			return new AxialCoord(a.q + b.q, a.r + b.r);
		}

		public static AxialCoord operator -(AxialCoord a, AxialCoord b)
		{
			return new AxialCoord(a.q - b.q, a.r - b.r);
		}

		public static bool operator ==(AxialCoord left, AxialCoord right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(AxialCoord left, AxialCoord right)
		{
			return left.Equals(right) == false;
		}
	}
}
