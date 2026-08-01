using UnityEngine;

namespace Battle
{
	// Suen Parvis has two server-authoritative loadouts that share action slots.
	// Keep the state-to-skill mapping here so generic battle input never has to
	// guess which duplicate CSV row is active.
	public sealed class SuenParvis : Suen
	{
		static readonly int ParvisOnHash = Animator.StringToHash("ParvisOn");
		static readonly int ParvisOnIdleHash = Animator.StringToHash("Base Layer.Suen_ParvisOn_Idle");
		static readonly int ParvisOffIdleHash = Animator.StringToHash("Base Layer.Suen_ParvisOff_Idle2");

		Animator _animator;
		bool _hasAppliedParvisState;
		bool _lastParvisOffState;

		public const string ParvisOffStatusKey = "SUEN_PARVIS_OFF";

		public bool IsParvisOff => Statuses.ContainsKey(ParvisOffStatusKey);

		protected override void OnPawnInitialized()
		{
			ApplyParvisAnimatorState();
		}

		protected override void OnPawnStateChanged()
		{
			// Installing applies SUEN_PARVIS_OFF and picking the equipment up removes it.
			ApplyParvisAnimatorState();
		}

		void ApplyParvisAnimatorState()
		{
			if (_animator == null)
				_animator = GetComponentInChildren<Animator>(true);

			if (_animator == null || _animator.runtimeAnimatorController == null)
				return;

			foreach (AnimatorControllerParameter parameter in _animator.parameters)
			{
				if (parameter.type != AnimatorControllerParameterType.Bool || parameter.nameHash != ParvisOnHash)
					continue;

				// The supplied controller's parameter is wired inversely: true takes
				// ParvisOn Idle to ParvisOff Idle, while false returns to ParvisOn.
				bool isParvisOff = IsParvisOff;
				bool stateChanged = _hasAppliedParvisState == false || _lastParvisOffState != isParvisOff;
				_animator.SetBool(ParvisOnHash, isParvisOff);
				if (stateChanged)
				{
					int idleHash = isParvisOff ? ParvisOffIdleHash : ParvisOnIdleHash;
					if (_animator.HasState(0, idleHash))
						_animator.Play(idleHash, 0, 0f);
				}

				_hasAppliedParvisState = true;
				_lastParvisOffState = isParvisOff;
				return;
			}

			Debug.LogWarning($"Suen Parvis animator is missing the ParvisOn bool. pawnId={PawnId}");
		}

		public bool TryGetActiveSkillKey(int actionSlot, out string skillKey)
		{
			skillKey = null;
			switch (actionSlot)
			{
				case 1: skillKey = "SUEN_PARVIS_MAN_IN_HELL"; return true;
				case 2: skillKey = IsParvisOff ? "SUEN_PARVIS_SIT_SHOT" : "SUEN_PARVIS_INSTALL"; return true;
				case 3: skillKey = IsParvisOff ? "SUEN_PARVIS_STAND_SHOT_OFF" : "SUEN_PARVIS_STAND_SHOT_ON"; return true;
				case 4: skillKey = "SUEN_PARVIS_POINT_BLANK"; return true;
				case 5: skillKey = IsParvisOff ? "SUEN_PARVIS_ROLL_SHOT" : "SUEN_PARVIS_YABAWI"; return true;
				case 6: skillKey = "SUEN_PARVIS_DARK_HAND"; return true;
				case 7: skillKey = "SUEN_PARVIS_PICKUP"; return true;
				default: return false;
			}
		}

		public bool TryGetSkillPresentation(int actionSlot, out string displayName, out string iconKey)
		{
			displayName = null;
			iconKey = null;
			switch (actionSlot)
			{
				case 1: displayName = "Man in Hell"; iconKey = "icon_suen_parvis_passive"; return true;
				case 2:
					displayName = IsParvisOff ? "Sit Shot" : "Install Parvis";
					iconKey = IsParvisOff ? "icon_suen_parvis_skill2" : "icon_suen_parvis_on_skill1";
					return true;
				case 3:
					displayName = "Stand Shot";
					iconKey = IsParvisOff ? "icon_suen_parvis_off_skill1" : "icon_suen_parvis_on_skill1";
					return true;
				case 4: displayName = "Point Blank"; iconKey = "icon_suen_parvis_skill3"; return true;
				case 5:
					displayName = IsParvisOff ? "Roll Shot" : "Yabawi";
					iconKey = IsParvisOff ? "icon_suen_parvis_off_skill4" : "icon_suen_parvis_on_skill4";
					return true;
				case 6: displayName = "Dark Hand"; iconKey = "icon_suen_parvis_ulti"; return true;
				case 7: displayName = "Pickup Parvis"; iconKey = "icon_suen_parvis_sub"; return true;
				default: return false;
			}
		}

		public static string GetStatusDisplayName(string statusKey)
		{
			if (string.Equals(statusKey, ParvisOffStatusKey, System.StringComparison.Ordinal))
				return "Parvis Installed";
			if (string.Equals(statusKey, "SUEN_PARVIS_YABAWI_SELF", System.StringComparison.Ordinal)
				|| string.Equals(statusKey, "SUEN_PARVIS_YABAWI_ALLY", System.StringComparison.Ordinal))
				return "Yabawi Evasion";

			return statusKey;
		}

		public static string GetStatusIconLabel(string statusKey)
		{
			if (string.Equals(statusKey, ParvisOffStatusKey, System.StringComparison.Ordinal))
				return "PVS";
			if (string.Equals(statusKey, "SUEN_PARVIS_YABAWI_SELF", System.StringComparison.Ordinal)
				|| string.Equals(statusKey, "SUEN_PARVIS_YABAWI_ALLY", System.StringComparison.Ordinal))
				return "EVA";

			return null;
		}
	}
}
