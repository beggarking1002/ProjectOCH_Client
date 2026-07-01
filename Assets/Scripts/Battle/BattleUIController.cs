using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace Battle
{
	[DisallowMultipleComponent]
	public sealed class BattleUIController : MonoBehaviour
	{
		BattleObjectManager _objectManager;
		Text _turnText;
		Text _modeText;

		public void Initialize(BattleObjectManager objectManager)
		{
			_objectManager = objectManager;
			EnsureEventSystem();
			BuildUi();
			Refresh();
		}

		void Update()
		{
			Refresh();
		}

		void BuildUi()
		{
			Canvas canvas = GetComponentInChildren<Canvas>();
			if (canvas == null)
			{
				GameObject canvasObject = new GameObject("BattleUI_Canvas");
				canvasObject.transform.SetParent(transform, false);
				canvas = canvasObject.AddComponent<Canvas>();
				canvas.renderMode = RenderMode.ScreenSpaceOverlay;
				canvas.sortingOrder = 1000;
				canvasObject.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
				canvasObject.AddComponent<GraphicRaycaster>();
			}

			GameObject panel = CreatePanel(canvas.transform);
			_turnText = CreateText(panel.transform, "TurnText", "Turn: -", new Vector2(10f, -10f), new Vector2(260f, 28f));
			_modeText = CreateText(panel.transform, "ModeText", "Mode: Move", new Vector2(10f, -42f), new Vector2(260f, 28f));

			CreateButton(panel.transform, "MoveButton", "Move", new Vector2(10f, -82f), () => SetMode(BattleActionMode.Move));
			CreateButton(panel.transform, "Skill1Button", "Skill1", new Vector2(10f, -124f), () => SetMode(BattleActionMode.Skill1));
			CreateButton(panel.transform, "Skill2Button", "Skill2", new Vector2(10f, -166f), () => SetMode(BattleActionMode.Skill2));
			CreateButton(panel.transform, "Skill3Button", "Skill3", new Vector2(10f, -208f), () => SetMode(BattleActionMode.Skill3));
			CreateButton(panel.transform, "EndTurnButton", "End Turn", new Vector2(10f, -250f), OnEndTurnClicked);
		}

		void Refresh()
		{
			if (_objectManager == null)
				return;

			if (_turnText != null)
			{
				string ownership = _objectManager.IsCurrentTurnLocal ? "Mine" : "Enemy";
				_turnText.text = $"Turn Pawn: {_objectManager.CurrentTurnPawnId} ({ownership})";
			}

			if (_modeText != null)
				_modeText.text = $"Mode: {_objectManager.ActionMode}";
		}

		void SetMode(BattleActionMode mode)
		{
			if (_objectManager == null)
				return;

			_objectManager.SetActionMode(mode);
			Refresh();
		}

		void OnEndTurnClicked()
		{
			if (_objectManager == null)
				return;

			_objectManager.DebugEndTurn();
			Refresh();
		}

		static GameObject CreatePanel(Transform parent)
		{
			GameObject panel = new GameObject("BattleActionPanel");
			panel.transform.SetParent(parent, false);

			RectTransform rect = panel.AddComponent<RectTransform>();
			rect.anchorMin = new Vector2(0f, 1f);
			rect.anchorMax = new Vector2(0f, 1f);
			rect.pivot = new Vector2(0f, 1f);
			rect.anchoredPosition = new Vector2(16f, -16f);
			rect.sizeDelta = new Vector2(280f, 300f);

			Image image = panel.AddComponent<Image>();
			image.color = new Color(0f, 0f, 0f, 0.55f);
			return panel;
		}

		static Text CreateText(Transform parent, string name, string text, Vector2 position, Vector2 size)
		{
			GameObject go = new GameObject(name);
			go.transform.SetParent(parent, false);

			RectTransform rect = go.AddComponent<RectTransform>();
			rect.anchorMin = new Vector2(0f, 1f);
			rect.anchorMax = new Vector2(0f, 1f);
			rect.pivot = new Vector2(0f, 1f);
			rect.anchoredPosition = position;
			rect.sizeDelta = size;

			Text label = go.AddComponent<Text>();
			label.text = text;
			label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
			label.fontSize = 18;
			label.color = Color.white;
			label.alignment = TextAnchor.MiddleLeft;
			return label;
		}

		static void CreateButton(Transform parent, string name, string label, Vector2 position, UnityEngine.Events.UnityAction onClick)
		{
			GameObject go = new GameObject(name);
			go.transform.SetParent(parent, false);

			RectTransform rect = go.AddComponent<RectTransform>();
			rect.anchorMin = new Vector2(0f, 1f);
			rect.anchorMax = new Vector2(0f, 1f);
			rect.pivot = new Vector2(0f, 1f);
			rect.anchoredPosition = position;
			rect.sizeDelta = new Vector2(160f, 34f);

			Image image = go.AddComponent<Image>();
			image.color = new Color(0.18f, 0.2f, 0.24f, 0.95f);

			Button button = go.AddComponent<Button>();
			button.onClick.AddListener(onClick);

			Text text = CreateText(go.transform, "Text", label, Vector2.zero, rect.sizeDelta);
			text.alignment = TextAnchor.MiddleCenter;
		}

		static void EnsureEventSystem()
		{
			EventSystem existing = FindFirstObjectByType<EventSystem>();
			if (existing != null)
			{
#if ENABLE_INPUT_SYSTEM
				StandaloneInputModule standalone = existing.GetComponent<StandaloneInputModule>();
				if (standalone != null)
					Destroy(standalone);

				if (existing.GetComponent<InputSystemUIInputModule>() == null)
					existing.gameObject.AddComponent<InputSystemUIInputModule>();
#endif
				return;
			}

			GameObject eventSystem = new GameObject("EventSystem");
			eventSystem.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
			eventSystem.AddComponent<InputSystemUIInputModule>();
#else
			eventSystem.AddComponent<StandaloneInputModule>();
#endif
		}
	}
}
