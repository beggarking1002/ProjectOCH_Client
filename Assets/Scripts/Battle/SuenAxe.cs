using UnityEngine;

namespace Battle
{
	// Presentation state for the server-authoritative Suen axe/sword kit.
	public sealed class SuenAxe : Suen
	{
		static readonly int AxeOnHash = Animator.StringToHash("AxeOn");

		Animator _animator;

		public const string AxeOffStatusKey = "SUEN_AXE_AXE_OFF";

		public bool IsAxeOff => Statuses.ContainsKey(AxeOffStatusKey);

		protected override void OnPawnInitialized()
		{
			ApplyAxeAnimatorState();
		}

		protected override void OnPawnStateChanged()
		{
			// The server's status delta is the source of truth: throwing the axe adds
			// AXE_OFF, while picking it up removes it.
			ApplyAxeAnimatorState();
		}

		void ApplyAxeAnimatorState()
		{
			if (_animator == null)
				_animator = GetComponentInChildren<Animator>(true);

			if (_animator == null || _animator.runtimeAnimatorController == null)
				return;

			foreach (AnimatorControllerParameter parameter in _animator.parameters)
			{
				if (parameter.type != AnimatorControllerParameterType.Bool || parameter.nameHash != AxeOnHash)
					continue;

				_animator.SetBool(AxeOnHash, IsAxeOff == false);
				return;
			}

			Debug.LogWarning($"Suen axe animator is missing the AxeOn bool. pawnId={PawnId}");
		}

		public bool TryGetSkillPresentation(int actionSlot, out string displayName, out string iconKey)
		{
			displayName = string.Empty;
			iconKey = string.Empty;
			switch (actionSlot)
			{
				case 2:
					displayName = IsAxeOff ? "아픈 손가락" : "돈벌이";
					iconKey = IsAxeOff ? "icon_suen_axeoff_skill1" : "icon_suen_axe_skill1";
					return true;
				case 3:
					displayName = "청소부";
					iconKey = "icon_suen_axe_skill3";
					return true;
				case 4:
					displayName = IsAxeOff ? "칵 퉤!" : "잔금 지불";
					iconKey = IsAxeOff ? "icon_suen_axeoff_skill2" : "icon_suen_axe_skill3";
					return true;
				case 5:
					displayName = "혼자 크는 남자";
					iconKey = "icon_suen_axe_skill4";
					return true;
				case 6:
					displayName = "최후의 생존자";
					iconKey = "icon_suen_axeoff_skill3";
					return true;
				case 7:
					displayName = "도끼 줍기";
					iconKey = "icon_suen_axe_sub";
					return true;
			}

			return false;
		}

		public static string GetStatusDisplayName(string statusKey)
		{
			switch (statusKey)
			{
				case "SUEN_AXE_AXE_OFF": return "AxeOff";
				case "SUEN_AXE_AXE_OFF_EVASION": return "도끼 없음 회피";
				case "SUEN_AXE_SPIT_ACCURACY_DOWN": return "침 명중 감소";
				case "SUEN_AXE_LONE_GROWING_MAN_ACTIVE": return "혼자 크는 남자";
				case "SUEN_AXE_LAST_MAN_FIRST_HIT_EVADE": return "첫 피격 회피";
				case "SUEN_AXE_LAST_MAN_EVASION": return "최후의 생존자 회피";
				case "SUEN_AXE_LAST_MAN_DAMAGE_REDUCTION": return "광역/도트 피해 감소";
				case "SUEN_AXE_LAST_MAN_INTERCEPT_GUARD": return "인접 아군 대신 방어";
				default: return statusKey;
			}
		}

		public static string GetStatusIconLabel(string statusKey)
		{
			switch (statusKey)
			{
				case "SUEN_AXE_AXE_OFF": return "AXE";
				case "SUEN_AXE_AXE_OFF_EVASION": return "EVA";
				case "SUEN_AXE_SPIT_ACCURACY_DOWN": return "ACC";
				case "SUEN_AXE_LONE_GROWING_MAN_ACTIVE": return "GRW";
				case "SUEN_AXE_LAST_MAN_FIRST_HIT_EVADE": return "1EV";
				case "SUEN_AXE_LAST_MAN_EVASION": return "EVA";
				case "SUEN_AXE_LAST_MAN_DAMAGE_REDUCTION": return "DR";
				case "SUEN_AXE_LAST_MAN_INTERCEPT_GUARD": return "GRD";
				default: return null;
			}
		}
	}
}
