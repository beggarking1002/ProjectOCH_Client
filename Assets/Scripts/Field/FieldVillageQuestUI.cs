using System.Text;
using App;
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

		string _villageId;
		bool _subscribed;
		bool _trackerMode;

		public void Show(FieldVillageUI villageUi, string villageId)
		{
			_trackerMode = false;
			_villageId = villageId;
			SetHeader("퀘스트 게시판", "돌아가기");
			backButton?.onClick.AddListener(ReturnToVillage);
			Subscribe();
			ClearButtons();
			SetStatus("의뢰 목록을 불러오는 중입니다.", false);
			if (GameRoot.Instance == null || GameRoot.Instance.Network.OpenVillageQuestBoard(_villageId) == false)
				SetStatus("의뢰 목록을 요청할 수 없습니다.", true);
		}

		public void ShowTracker()
		{
			_trackerMode = true;
			_villageId = string.Empty;
			SetHeader("진행 중인 퀘스트", "닫기");
			backButton?.onClick.AddListener(ReturnToVillage);
			Subscribe();
			ClearButtons();
			SetStatus("진행 중인 퀘스트를 불러오는 중입니다.", false);
			if (GameRoot.Instance == null || GameRoot.Instance.Network.OpenQuestTracker() == false)
				SetStatus("퀘스트 정보를 요청할 수 없습니다.", true);
		}

		void OnDestroy()
		{
			Unsubscribe();
			backButton?.onClick.RemoveListener(ReturnToVillage);
			ClearButtonListeners();
		}

		void Subscribe()
		{
			if (_subscribed || GameRoot.Instance == null)
				return;
			GameRoot.Instance.Network.VillageQuestStateReceived += OnQuestStateReceived;
			GameRoot.Instance.Network.QuestTrackerStateReceived += OnQuestTrackerStateReceived;
			GameRoot.Instance.Network.PlayerDataResetReceived += OnPlayerDataResetReceived;
			_subscribed = true;
		}

		void Unsubscribe()
		{
			if (!_subscribed || GameRoot.Instance == null)
				return;
			GameRoot.Instance.Network.VillageQuestStateReceived -= OnQuestStateReceived;
			GameRoot.Instance.Network.QuestTrackerStateReceived -= OnQuestTrackerStateReceived;
			GameRoot.Instance.Network.PlayerDataResetReceived -= OnPlayerDataResetReceived;
			_subscribed = false;
		}

		void OnPlayerDataResetReceived(Protocol.S_RESET_PLAYER_DATA packet)
		{
			if (packet == null || packet.Success == false || GameRoot.Instance == null)
				return;
			SetStatus("초기화된 의뢰 목록을 불러오는 중입니다.", false);
			GameRoot.Instance.Network.OpenVillageQuestBoard(_villageId);
		}

		void OnQuestStateReceived(Protocol.S_VILLAGE_QUEST_STATE packet)
		{
			if (_trackerMode)
				return;
			if (packet == null || packet.VillageId != _villageId)
				return;
			if (!packet.Success)
			{
				SetStatus(string.IsNullOrWhiteSpace(packet.Reason) ? "의뢰 요청에 실패했습니다." : packet.Reason, true);
				return;
			}
			Render(packet);
		}

		void OnQuestTrackerStateReceived(Protocol.S_QUEST_TRACKER_STATE packet)
		{
			if (_trackerMode == false || packet == null)
				return;
			if (packet.Success == false)
			{
				SetStatus(string.IsNullOrWhiteSpace(packet.Reason) ? "퀘스트 정보를 불러오지 못했습니다." : packet.Reason, true);
				return;
			}

			ClearButtons();
			int visibleCount = Mathf.Min(questButtons != null ? questButtons.Length : 0, packet.Quests.Count);
			for (int index = 0; index < visibleCount; index++)
			{
				Button button = questButtons[index];
				if (button == null)
					continue;
				button.gameObject.SetActive(true);
				button.interactable = false;
				Text label = button.GetComponentInChildren<Text>(true);
				if (label != null)
				{
					label.text = BuildTrackerLabel(packet.Quests[index]);
					label.fontSize = Mathf.Min(label.fontSize, 16);
				}
			}

			SetStatus(packet.Quests.Count == 0 ? "현재 진행 중인 퀘스트가 없습니다." : "목표를 완료한 뒤 해당 마을에서 보상을 수령하세요.", false);
		}

		void Render(Protocol.S_VILLAGE_QUEST_STATE packet)
		{
			ClearButtons();
			int visibleCount = Mathf.Min(questButtons != null ? questButtons.Length : 0, packet.Quests.Count);
			for (int index = 0; index < visibleCount; index++)
			{
				Protocol.VillageQuestInfo quest = packet.Quests[index];
				Button button = questButtons[index];
				if (button == null)
					continue;
				button.gameObject.SetActive(true);
				Text label = button.GetComponentInChildren<Text>(true);
				if (label != null)
				{
					label.text = BuildQuestLabel(quest);
					label.fontSize = Mathf.Min(label.fontSize, 16);
					label.horizontalOverflow = HorizontalWrapMode.Wrap;
					label.verticalOverflow = VerticalWrapMode.Truncate;
				}

				if (quest.CanAccept)
				{
					string questId = quest.QuestId;
					button.interactable = true;
					button.onClick.AddListener(() => Accept(questId));
				}
				else if (quest.CanClaim)
				{
					string questId = quest.QuestId;
					button.interactable = true;
					button.onClick.AddListener(() => Claim(questId));
				}
				else if (quest.Status == "COMPLETED")
				{
					button.interactable = true;
					button.onClick.AddListener(ShowAlreadyCompleted);
				}
				else
				{
					button.interactable = false;
				}
			}

			SetStatus(packet.Quests.Count == 0 ? "이 마을에서 확인할 수 있는 의뢰가 없습니다." : ActionMessage(packet.Action), false);
		}

		void ShowAlreadyCompleted()
		{
			SetStatus("이미 보상을 수령한 완료 의뢰입니다.", false);
		}

		static string BuildQuestLabel(Protocol.VillageQuestInfo quest)
		{
			StringBuilder builder = new StringBuilder();
			builder.Append('[').Append(StatusLabel(quest.Status)).Append("] ").Append(quest.DisplayName);
			if (!string.IsNullOrWhiteSpace(quest.Description))
				builder.Append('\n').Append(quest.Description);
			foreach (Protocol.QuestObjectiveProgressInfo objective in quest.Objectives)
				builder.Append("\n· ").Append(ObjectiveLabel(objective)).Append(' ').Append(objective.Progress).Append('/').Append(objective.RequiredCount);
			builder.Append("\n완료: ").Append(quest.CompletionVillageName);
			if (quest.RewardDescriptions.Count > 0)
				builder.Append(" · 보상 ").Append(string.Join(", ", quest.RewardDescriptions));
			if (quest.CanAccept) builder.Append("\n클릭하여 수락");
			else if (quest.CanClaim) builder.Append("\n클릭하여 보상 수령");
			else if (quest.Status == "LIMIT_REACHED") builder.Append("\n동시에 진행할 수 있는 의뢰는 최대 3개입니다.");
			else if (quest.Status == "COMPLETED") builder.Append("\n보상 수령 완료");
			return builder.ToString();
		}

		static string BuildTrackerLabel(Protocol.VillageQuestInfo quest)
		{
			StringBuilder builder = new StringBuilder();
			builder.Append('[').Append(StatusLabel(quest.Status)).Append("] ").Append(quest.DisplayName);
			if (!string.IsNullOrWhiteSpace(quest.Description))
				builder.Append('\n').Append(quest.Description);
			foreach (Protocol.QuestObjectiveProgressInfo objective in quest.Objectives)
				builder.Append("\n· ").Append(ObjectiveLabel(objective)).Append(' ').Append(objective.Progress).Append('/').Append(objective.RequiredCount);
			builder.Append("\n완료 보고: ").Append(quest.CompletionVillageName);
			if (quest.RewardDescriptions.Count > 0)
				builder.Append("\n보상: ").Append(string.Join(", ", quest.RewardDescriptions));
			return builder.ToString();
		}

		static string ObjectiveLabel(Protocol.QuestObjectiveProgressInfo objective)
		{
			switch (objective.ObjectiveType)
			{
				case "VISIT_VILLAGE": return $"{objective.TargetVillageName} 방문";
				case "OWN_ITEM": return $"{objective.TargetItemName} 보유";
				case "DELIVER_ITEM": return $"{objective.TargetVillageName}에 {objective.TargetItemName} 전달";
				case "BUY_ITEM": return $"{objective.TargetItemName} 구매";
				case "SELL_TRADE_GOOD": return $"{objective.TargetVillageName}에 {objective.TargetItemName} 판매";
				default: return objective.Description;
			}
		}

		static string StatusLabel(string status)
		{
			switch (status)
			{
				case "AVAILABLE": return "수락 가능";
				case "ACTIVE": return "진행 중";
				case "READY": return "완료 보고";
				case "LIMIT_REACHED": return "수락 제한";
				case "COMPLETED": return "완료";
				default: return "잠김";
			}
		}

		void Accept(string questId)
		{
			SetStatus("의뢰를 수락하는 중입니다.", false);
			if (GameRoot.Instance == null || !GameRoot.Instance.Network.AcceptQuest(questId))
				SetStatus("의뢰 수락 요청을 보내지 못했습니다.", true);
		}

		void Claim(string questId)
		{
			SetStatus("보상을 확인하는 중입니다.", false);
			if (GameRoot.Instance == null || !GameRoot.Instance.Network.ClaimQuestReward(questId))
				SetStatus("보상 수령 요청을 보내지 못했습니다.", true);
		}

		void ClearButtons()
		{
			ClearButtonListeners();
			if (questButtons == null)
				return;
			foreach (Button button in questButtons)
			{
				if (button == null) continue;
				button.interactable = false;
				button.gameObject.SetActive(false);
			}
		}

		void ClearButtonListeners()
		{
			if (questButtons == null)
				return;
			foreach (Button button in questButtons)
				button?.onClick.RemoveAllListeners();
		}

		void SetStatus(string message, bool error)
		{
			if (statusText == null) return;
			statusText.text = message;
			statusText.color = error ? new Color(0.85f, 0.24f, 0.18f) : new Color(0.25f, 0.12f, 0.04f);
		}

		void SetHeader(string title, string backLabel)
		{
			Text titleText = transform.Find("QuestWindow/Title")?.GetComponent<Text>();
			if (titleText != null)
				titleText.text = title;
			Text buttonLabel = backButton != null ? backButton.GetComponentInChildren<Text>(true) : null;
			if (buttonLabel != null)
				buttonLabel.text = backLabel;
		}

		static string ActionMessage(string action)
		{
			switch (action)
			{
				case "accept": return "의뢰를 수락했습니다.";
				case "claim": return "보상을 수령했습니다.";
				default: return "의뢰를 선택하세요.";
			}
		}

		void ReturnToVillage()
		{
			Unsubscribe();
			Addressables.ReleaseInstance(gameObject);
		}
	}
}
