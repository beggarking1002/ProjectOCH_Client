namespace Battle
{
	// The server owns stance, intercept, taunt, and duel state. This component
	// translates that state into the Sword/Shield-specific action and status UI.
	public sealed class AlenSwordShield : BattlePawn
	{
		public const string CarbasGuardStatusKey = "ALEN_SHIELD_CARBAS_GUARD";
		public const string RoyalGuardStatusKey = "ALEN_SHIELD_ROYAL_GUARD";

		public bool IsCarbasGuardActive => Statuses.ContainsKey(CarbasGuardStatusKey);
		public bool IsRoyalGuardActive => Statuses.ContainsKey(RoyalGuardStatusKey);

		public override void TriggerSkill(int skillSlot)
		{
			// The supplied Sword/Shield controller has an attack (Skill1) and a
			// guard/support (Skill2) gesture. State is still applied only by the
			// authoritative pawn delta after this presentation is played.
			switch (skillSlot)
			{
				case 3: // stance
				case 4: // responsibility
				case 6: // duel master
				case 7: // morale boost
					TriggerSkill("Skill2");
					return;
				case 2: // effort
				case 5: // pride
					TriggerSkill("Skill1");
					return;
			}

			base.TriggerSkill(skillSlot);
		}

		public bool TryGetSkillPresentation(int actionSlot, out string displayName, out string iconKey)
		{
			displayName = null;
			iconKey = null;
			switch (actionSlot)
			{
				case 1: displayName = "카르바스 가문의 의지"; iconKey = "icon_alen_shield_passive"; return true;
				case 2: displayName = "노력"; iconKey = "icon_alen_shield_skill1"; return true;
				case 3: displayName = "황실 기사단식 방패술/카르바스식 방검술"; iconKey = "icon_alen_shield_skill2"; return true;
				case 4: displayName = "책임감"; iconKey = "icon_alen_shield_skill3"; return true;
				case 5: displayName = "자존심"; iconKey = "icon_alen_shield_skill4"; return true;
				case 6: displayName = "결투의 대가"; iconKey = "icon_alen_shield_ulti"; return true;
				case 7: displayName = "사기진작"; iconKey = "icon_alen_shield_sub"; return true;
				default: return false;
			}
		}

		public static string GetStatusDisplayName(string statusKey)
		{
			switch (statusKey)
			{
				case "ALEN_SHIELD_CARBAS_WILL": return "카르바스 가문의 의지";
				case CarbasGuardStatusKey: return "카르바스식 방패술";
				case RoyalGuardStatusKey: return "황실 기사단식 방패술";
				case "ALEN_SHIELD_RESPONSIBILITY": return "책임감";
				case "ALEN_SHIELD_RESPONSIBILITY_DEFENSE": return "책임감 방어력";
				case "ALEN_SHIELD_TAUNT": return "자존심 도발";
				case "ALEN_SHIELD_DUEL_MASTER": return "결투의 대가";
				case "ALEN_SHIELD_DUEL_MASTER_STR": return "결투의 대가 근력 전환";
				default: return statusKey;
			}
		}

		public static string GetStatusIconLabel(string statusKey)
		{
			switch (statusKey)
			{
				case "ALEN_SHIELD_CARBAS_WILL": return "WIL";
				case CarbasGuardStatusKey: return "CTR";
				case RoyalGuardStatusKey: return "GRD";
				case "ALEN_SHIELD_RESPONSIBILITY": return "INT";
				case "ALEN_SHIELD_RESPONSIBILITY_DEFENSE": return "DEF";
				case "ALEN_SHIELD_TAUNT": return "TNT";
				case "ALEN_SHIELD_DUEL_MASTER": return "DUE";
				case "ALEN_SHIELD_DUEL_MASTER_STR": return "STR";
				default: return null;
			}
		}
	}
}
