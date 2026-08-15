using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.UI;

namespace Field
{
	[DisallowMultipleComponent]
	public sealed class FieldVillageQuestUI : MonoBehaviour
	{
		[SerializeField] Button[] questButtons;
		[SerializeField] Button backButton;
		[SerializeField] Text statusText;

		FieldVillageUI _villageUi;
		bool _hasAcceptedQuest;

		public void Show(FieldVillageUI villageUi, bool hasAcceptedQuest)
		{
			_villageUi = villageUi;
			_hasAcceptedQuest = hasAcceptedQuest;
			backButton.onClick.AddListener(ReturnToVillage);

			for (int i = 0; i < questButtons.Length; i++)
			{
				int questIndex = i;
				questButtons[i].interactable = !hasAcceptedQuest;
				questButtons[i].onClick.AddListener(() => AcceptQuest(questIndex));
			}

			if (statusText != null)
				statusText.text = hasAcceptedQuest ? "\uC218\uC8FC \uC911 \uD018\uC2A4\uD2B8\uAC00 \uC788\uC2B5\uB2C8\uB2E4." : "\uD018\uC2A4\uD2B8\uB294 \uD558\uB098\uB9CC \uC218\uC8FC\uD560 \uC218 \uC788\uC2B5\uB2C8\uB2E4.";
		}

		void AcceptQuest(int questIndex)
		{
			if (_hasAcceptedQuest)
				return;

			_hasAcceptedQuest = true;
			_villageUi?.MarkQuestAccepted();
			for (int i = 0; i < questButtons.Length; i++)
				questButtons[i].interactable = false;

			if (statusText != null)
				statusText.text = $"\uB09C\uC774\uB3C4 {questIndex + 1} \uD018\uC2A4\uD2B8 \uC218\uC8FC \uC644\uB8CC · \uC804\uD22C 1\uD68C \uC2B9\uB9AC";
		}

		void ReturnToVillage()
		{
			backButton.onClick.RemoveListener(ReturnToVillage);
			for (int i = 0; i < questButtons.Length; i++)
				questButtons[i].onClick.RemoveAllListeners();

			Addressables.ReleaseInstance(gameObject);
		}
	}
}
