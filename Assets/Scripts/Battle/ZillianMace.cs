namespace Battle
{
	// Damage, healing, barriers, blind, and stun remain server-authoritative.
	// This class owns only the Mace-specific labels and the two supplied gestures.
	public sealed class ZillianMace : BattlePawn
	{
		public override void TriggerSkill(int skillSlot)
		{
			if (skillSlot == 2 || skillSlot == 3)
			{
				TriggerSkill("Skill1");
				return;
			}

			if (skillSlot >= 4 && skillSlot <= 7)
			{
				TriggerSkill("Skill2");
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
				case 1: displayName = "내가 성녀라니"; iconKey = "icon_zillian_mace_passive"; return true;
				case 2: displayName = "기초 빠따질"; iconKey = "icon_zillian_mace_skill1"; return true;
				case 3: displayName = "오지마!"; iconKey = "icon_zillian_mace_skill2"; return true;
				case 4: displayName = "눈부신 눈부심"; iconKey = "icon_zillian_mace_skill3"; return true;
				case 5: displayName = "원하지 않은 권능"; iconKey = "icon_zillian_mace_skill4"; return true;
				case 6: displayName = "아무도 죽지마!"; iconKey = "icon_zillian_mace_ulti"; return true;
				case 7: displayName = "나 돌아갈래"; iconKey = "icon_zillian_mace_sub"; return true;
				default: return false;
			}
		}
	}

}
