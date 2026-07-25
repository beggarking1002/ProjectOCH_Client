namespace Battle
{
	// Alen's server state is applied by BattlePawn. This class owns only the
	// character-specific presentation used by the battle action/status UI.
	public sealed class AlenSpear : BattlePawn
	{
		public bool TryGetSkillPresentation(int actionSlot, out string displayName, out string iconKey)
		{
			displayName = null;
			iconKey = null;
			switch (actionSlot)
			{
				case 1: displayName = "카르바스의 의지"; iconKey = "icon_alen_spear_passive"; return true;
				case 2: displayName = "라이언하트식 창술 1번동작"; iconKey = "icon_alen_spear_skill1"; return true;
				case 3: displayName = "라이언하트식 창술 2번동작"; iconKey = "icon_alen_spear_skill2"; return true;
				case 4: displayName = "라이언하트식 창술 3번동작"; iconKey = "icon_alen_spear_skill3"; return true;
				case 5: displayName = "파수꾼"; iconKey = "icon_alen_spear_skill4"; return true;
				case 6: displayName = "돌격 명령"; iconKey = "icon_alen_spear_ulti"; return true;
				case 7: displayName = "사기 진작"; iconKey = "icon_alen_spear_sub"; return true;
				default: return false;
			}
		}

		public static string GetStatusDisplayName(string statusKey)
		{
			switch (statusKey)
			{
				case "ALEN_SPEAR_SENTINEL": return "파수꾼";
				case "ALEN_SPEAR_CHARGE_COMMAND_MOVE": return "돌격 명령 이동";
				default: return statusKey;
			}
		}

		public static string GetStatusIconLabel(string statusKey)
		{
			switch (statusKey)
			{
				case "ALEN_SPEAR_SENTINEL": return "SEN";
				case "ALEN_SPEAR_CHARGE_COMMAND_MOVE": return "MOV";
				default: return null;
			}
		}
	}
}
