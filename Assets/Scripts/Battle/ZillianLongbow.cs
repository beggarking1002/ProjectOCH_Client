namespace Battle
{
	// Server state is owned by BattlePawn. Zillian keeps only her class-specific
	// slot text and the two animator gestures used by the longbow visual.
	public sealed class ZillianLongbow : BattlePawn
	{
		public override void TriggerSkill(int skillSlot)
		{
			if (skillSlot == 4)
			{
				TriggerSkill("Skill2");
				return;
			}

			if (skillSlot >= 2 && skillSlot <= 7)
			{
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
				case 1: displayName = "내가 성녀라니"; iconKey = "icon_zillian_longbow_passive"; return true;
				case 2: displayName = "기초 활질"; iconKey = "icon_zillian_longbow_skill1"; return true;
				case 3: displayName = "조금 거친 치유법"; iconKey = "icon_zillian_longbow_skill2"; return true;
				case 4: displayName = "비장의 몽둥이질"; iconKey = "icon_zillian_longbow_skill3"; return true;
				case 5: displayName = "살고싶어!"; iconKey = "icon_zillian_longbow_skill4"; return true;
				case 6: displayName = "퍼뜩 인나라!"; iconKey = "icon_zillian_longbow_ulti"; return true;
				case 7: displayName = "나 돌아갈래"; iconKey = "icon_zillian_longbow_sub"; return true;
				default: return false;
			}
		}
	}
}
