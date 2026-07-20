using System;

namespace Battle
{
	// Compatibility bridge for scenes or prefabs created before the BattlePawn hierarchy.
	// New runtime pawns are created as BattlePawn-derived character classes.
	[Obsolete("Use BattlePawn or a character-specific subclass instead.")]
	public sealed class BattlePawnController : BattlePawn
	{
	}
}
