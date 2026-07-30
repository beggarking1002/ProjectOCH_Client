using System;
using System.Collections;
using System.Collections.Generic;
using Protocol;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;

namespace Battle
{
	/// <summary>
	/// Presentation-only view of the server-authoritative upcoming turn queue.
	/// The eight existing prefab slots are reused so a normal turn can rotate them,
	/// while resyncs match portraits by pawn id and animate only the changed entries.
	/// </summary>
	public sealed class BattleTurnQueueView
	{
		const int QueueSize = 8;
		const float PortraitWidth = 48f;
		const float PortraitHeight = 68f;
		const float PortraitSpacing = 15f;
		const float AnimationSeconds = 0.35f;
		const float FallingDistance = 90f;

		readonly MonoBehaviour _host;
		readonly Transform _root;
		readonly Func<ulong, PawnClass> _pawnClassResolver;
		readonly List<PortraitSlot> _slots = new List<PortraitSlot>(QueueSize);
		readonly HashSet<ulong> _knownDeadPawnIds = new HashSet<ulong>();
		readonly List<GameObject> _activeGhosts = new List<GameObject>();
		Coroutine _animation;

		public BattleTurnQueueView(MonoBehaviour host, Transform root, Func<ulong, PawnClass> pawnClassResolver)
		{
			_host = host;
			_root = root;
			_pawnClassResolver = pawnClassResolver;
			ConfigureSlots();
		}

		public void Apply(BattleTurnQueueUpdate update)
		{
			if (update == null || update.PawnIds == null || update.PawnIds.Count == 0 || _slots.Count != QueueSize)
				return;

			if (update.DeadPawnIds != null)
			{
				foreach (ulong pawnId in update.DeadPawnIds)
					MarkPawnDead(pawnId);
			}

			if (update.Kind == BattleTurnQueueUpdateKind.Initialize)
			{
				StopAnimation();
				ApplyImmediate(update.PawnIds);
				return;
			}

			if (update.Kind == BattleTurnQueueUpdateKind.Shift && update.PawnIds.Count == QueueSize)
				AnimateShift(update.PawnIds);
			else
				AnimateResync(update.PawnIds);
		}

		public void MarkPawnDead(ulong pawnId)
		{
			if (pawnId != 0)
				_knownDeadPawnIds.Add(pawnId);
		}

		public void Dispose()
		{
			StopAnimation();
		}

		void ConfigureSlots()
		{
			if (_root == null)
				return;

			HorizontalLayoutGroup layout = _root.GetComponent<HorizontalLayoutGroup>();
			if (layout != null)
				layout.enabled = false;

			for (int i = 1; i <= QueueSize; i++)
			{
				Transform slotTransform = FindDeepChild(_root, $"TurnPortraitSlot_{i:00}");
				if (slotTransform == null)
				{
					Debug.LogWarning($"Missing turn queue portrait slot {i}.");
					continue;
				}

				PortraitSlot slot = new PortraitSlot(slotTransform);
				slot.Configure(PortraitWidth, PortraitHeight);
				slot.SetPosition(GetSlotPosition(_slots.Count));
				_slots.Add(slot);
			}
		}

		void ApplyImmediate(IReadOnlyList<ulong> pawnIds)
		{
			for (int i = 0; i < _slots.Count; i++)
			{
				bool exists = i < pawnIds.Count && pawnIds[i] != 0;
				_slots[i].Root.gameObject.SetActive(exists);
				if (exists)
					_slots[i].SetPawn(pawnIds[i], ResolvePortraitKey(pawnIds[i]));

				_slots[i].SetPosition(GetSlotPosition(i));
				_slots[i].SetAlpha(1f);
			}
		}

		void AnimateShift(IReadOnlyList<ulong> snapshot)
		{
			StopAnimation();
			PortraitSlot leaving = _slots[0];
			GameObject historyGhost = CreateGhost(leaving, leaving.PawnId);
			List<PortraitMotion> motions = new List<PortraitMotion>(QueueSize);
			for (int i = 1; i < QueueSize; i++)
				motions.Add(new PortraitMotion(_slots[i], _slots[i].Position, GetSlotPosition(i - 1), 1f, 1f));

			_slots.RemoveAt(0);
			_slots.Add(leaving);
			leaving.SetPawn(snapshot[QueueSize - 1], ResolvePortraitKey(snapshot[QueueSize - 1]));
			leaving.Root.gameObject.SetActive(true);
			Vector2 enteringPosition = GetSlotPosition(QueueSize - 1) + new Vector2(PortraitWidth + PortraitSpacing, 0f);
			leaving.SetPosition(enteringPosition);
			leaving.SetAlpha(0f);
			motions.Add(new PortraitMotion(leaving, enteringPosition, GetSlotPosition(QueueSize - 1), 0f, 1f));

			_animation = _host.StartCoroutine(Animate(motions, new List<GameObject> { historyGhost }));
		}

		void AnimateResync(IReadOnlyList<ulong> snapshot)
		{
			StopAnimation();
			List<PortraitSlot> oldSlots = new List<PortraitSlot>(_slots);
			List<bool> oldSlotsUsed = new List<bool>(QueueSize);
			for (int i = 0; i < oldSlots.Count; i++)
				oldSlotsUsed.Add(false);

			List<PortraitSlot> nextSlots = new List<PortraitSlot>(QueueSize);
			List<int> unmatchedTargetIndices = new List<int>();
			for (int targetIndex = 0; targetIndex < QueueSize; targetIndex++)
			{
				ulong targetPawnId = targetIndex < snapshot.Count ? snapshot[targetIndex] : 0;
				int matchingOldIndex = FindUnusedSlot(oldSlots, oldSlotsUsed, targetPawnId);
				if (matchingOldIndex >= 0)
				{
					oldSlotsUsed[matchingOldIndex] = true;
					nextSlots.Add(oldSlots[matchingOldIndex]);
				}
				else
				{
					nextSlots.Add(null);
					unmatchedTargetIndices.Add(targetIndex);
				}
			}

			List<PortraitSlot> reusableSlots = new List<PortraitSlot>();
			List<GameObject> leavingGhosts = new List<GameObject>();
			for (int i = 0; i < oldSlots.Count; i++)
			{
				if (oldSlotsUsed[i])
					continue;

				PortraitSlot oldSlot = oldSlots[i];
				if (oldSlot.PawnId != 0)
					leavingGhosts.Add(CreateGhost(oldSlot, oldSlot.PawnId));
				reusableSlots.Add(oldSlot);
			}

			List<PortraitMotion> motions = new List<PortraitMotion>(QueueSize);
			for (int i = 0; i < QueueSize; i++)
			{
				ulong targetPawnId = i < snapshot.Count ? snapshot[i] : 0;
				PortraitSlot slot = nextSlots[i];
				if (slot == null)
				{
					slot = reusableSlots[0];
					reusableSlots.RemoveAt(0);
					nextSlots[i] = slot;
					slot.SetPawn(targetPawnId, ResolvePortraitKey(targetPawnId));
					slot.Root.gameObject.SetActive(targetPawnId != 0);
					Vector2 enteringPosition = GetSlotPosition(QueueSize - 1) + new Vector2(PortraitWidth + PortraitSpacing, 0f);
					slot.SetPosition(enteringPosition);
					slot.SetAlpha(0f);
					motions.Add(new PortraitMotion(slot, enteringPosition, GetSlotPosition(i), 0f, 1f));
				}
				else
				{
					slot.Root.gameObject.SetActive(targetPawnId != 0);
					motions.Add(new PortraitMotion(slot, slot.Position, GetSlotPosition(i), 1f, 1f));
				}
			}

			_slots.Clear();
			_slots.AddRange(nextSlots);
			_animation = _host.StartCoroutine(Animate(motions, leavingGhosts));
		}

		static int FindUnusedSlot(IReadOnlyList<PortraitSlot> slots, IReadOnlyList<bool> used, ulong pawnId)
		{
			if (pawnId == 0)
				return -1;

			for (int i = 0; i < slots.Count; i++)
			{
				if (used[i] == false && slots[i].PawnId == pawnId)
					return i;
			}

			return -1;
		}

		IEnumerator Animate(List<PortraitMotion> motions, List<GameObject> ghosts)
		{
			float elapsed = 0f;
			while (elapsed < AnimationSeconds)
			{
				elapsed += Time.unscaledDeltaTime;
				float progress = Mathf.Clamp01(elapsed / AnimationSeconds);
				float eased = Mathf.SmoothStep(0f, 1f, progress);
				foreach (PortraitMotion motion in motions)
				{
					motion.Slot.SetPosition(Vector2.Lerp(motion.StartPosition, motion.EndPosition, eased));
					motion.Slot.SetAlpha(Mathf.Lerp(motion.StartAlpha, motion.EndAlpha, eased));
				}

				foreach (GameObject ghost in ghosts)
				{
					if (ghost == null)
						continue;
					RectTransform rect = ghost.GetComponent<RectTransform>();
					CanvasGroup canvasGroup = ghost.GetComponent<CanvasGroup>();
					rect.anchoredPosition += Vector2.down * (FallingDistance * Time.unscaledDeltaTime / AnimationSeconds);
					canvasGroup.alpha = 1f - eased;
				}

				yield return null;
			}

			foreach (PortraitMotion motion in motions)
			{
				motion.Slot.SetPosition(motion.EndPosition);
				motion.Slot.SetAlpha(motion.EndAlpha);
			}
			foreach (GameObject ghost in ghosts)
			{
				if (ghost != null)
				{
					UnityEngine.Object.Destroy(ghost);
					_activeGhosts.Remove(ghost);
				}
			}

			_animation = null;
		}

		GameObject CreateGhost(PortraitSlot source, ulong pawnId)
		{
			GameObject ghost = UnityEngine.Object.Instantiate(source.Root.gameObject, _root);
			ghost.name = _knownDeadPawnIds.Contains(pawnId)
				? $"TurnPortraitDefeated_{pawnId}"
				: $"TurnHistory_{pawnId}";
			RectTransform rect = ghost.GetComponent<RectTransform>();
			rect.SetAsLastSibling();
			rect.anchoredPosition = source.Position;
			CanvasGroup group = ghost.GetComponent<CanvasGroup>() ?? ghost.AddComponent<CanvasGroup>();
			group.alpha = 1f;
			group.blocksRaycasts = false;
			_activeGhosts.Add(ghost);
			return ghost;
		}

		void StopAnimation()
		{
			if (_animation != null && _host != null)
				_host.StopCoroutine(_animation);
			_animation = null;
			foreach (GameObject ghost in _activeGhosts)
			{
				if (ghost != null)
					UnityEngine.Object.Destroy(ghost);
			}
			_activeGhosts.Clear();
		}

		Vector2 GetSlotPosition(int index)
		{
			float totalWidth = (PortraitWidth * QueueSize) + (PortraitSpacing * (QueueSize - 1));
			float startX = -totalWidth * 0.5f + PortraitWidth * 0.5f;
			return new Vector2(startX + index * (PortraitWidth + PortraitSpacing), 0f);
		}

		string ResolvePortraitKey(ulong pawnId)
		{
			switch (_pawnClassResolver != null ? _pawnClassResolver(pawnId) : PawnClass.None)
			{
				case PawnClass.SuenAxeSword:
				case PawnClass.SuenParvis: return "portrait_suen";
				case PawnClass.BeigeFire:
				case PawnClass.BeigeIce: return "portrait_beige";
				case PawnClass.ZillianLongbow:
				case PawnClass.ZillianMace: return "portrait_zillian";
				case PawnClass.AlenSpear:
				case PawnClass.AlenSwordShield: return "portrait_alen";
				case PawnClass.SeraNecromancer:
				case PawnClass.SeraWarlock: return "portrait_sera";
				case PawnClass.DarkhandSword: return "portrait_odo";
				default: return string.Empty;
			}
		}

		static Transform FindDeepChild(Transform parent, string childName)
		{
			if (parent == null)
				return null;
			if (parent.name == childName)
				return parent;

			for (int i = 0; i < parent.childCount; i++)
			{
				Transform result = FindDeepChild(parent.GetChild(i), childName);
				if (result != null)
					return result;
			}

			return null;
		}

		sealed class PortraitSlot
		{
			readonly Image _portraitImage;
			readonly CanvasGroup _canvasGroup;
			int _spriteRequestVersion;

			public readonly RectTransform Root;
			public ulong PawnId { get; private set; }
			public Vector2 Position => Root.anchoredPosition;

			public PortraitSlot(Transform transform)
			{
				Root = transform as RectTransform;
				_portraitImage = transform.GetComponent<Image>();
				_canvasGroup = transform.GetComponent<CanvasGroup>() ?? transform.gameObject.AddComponent<CanvasGroup>();
				_canvasGroup.blocksRaycasts = false;
			}

			public void Configure(float width, float height)
			{
				Root.anchorMin = new Vector2(0.5f, 0.5f);
				Root.anchorMax = new Vector2(0.5f, 0.5f);
				Root.pivot = new Vector2(0.5f, 0.5f);
				Root.sizeDelta = new Vector2(width, height);
				LayoutElement layoutElement = Root.GetComponent<LayoutElement>();
				if (layoutElement != null)
					layoutElement.ignoreLayout = true;

				if (_portraitImage != null)
					_portraitImage.preserveAspect = true;
				foreach (Image image in Root.GetComponentsInChildren<Image>(true))
				{
					image.raycastTarget = false;
					// The root contains the portrait; its child is the decorative gold frame.
					// Both must use the same 366:512-shaped rect, otherwise the old square
					// frame hangs outside the portrait after a queue update.
					if (image != _portraitImage)
					{
						RectTransform frameRect = image.rectTransform;
						frameRect.anchorMin = Vector2.zero;
						frameRect.anchorMax = Vector2.one;
						frameRect.offsetMin = Vector2.zero;
						frameRect.offsetMax = Vector2.zero;
					}
				}
			}

			public void SetPawn(ulong pawnId, string portraitKey)
			{
				PawnId = pawnId;
				int requestVersion = ++_spriteRequestVersion;
				if (_portraitImage == null)
					return;

				// The prefab's old placeholder art differs by slot. Never show that art
				// while Addressables is resolving a real pawn portrait; the decorative
				// child frame remains visible as the common loading/empty state.
				_portraitImage.sprite = null;
				_portraitImage.enabled = false;
				if (string.IsNullOrWhiteSpace(portraitKey))
					return;

				_ = LoadSpriteAsync(portraitKey, requestVersion);
			}

			async System.Threading.Tasks.Task LoadSpriteAsync(string portraitKey, int requestVersion)
			{
				Sprite sprite = await BattlePortraitSpriteCache.LoadAsync(portraitKey);
				if (requestVersion != _spriteRequestVersion || _portraitImage == null)
					return;

				_portraitImage.sprite = sprite;
				_portraitImage.enabled = sprite != null;
			}

			public void SetPosition(Vector2 position) => Root.anchoredPosition = position;
			public void SetAlpha(float alpha) => _canvasGroup.alpha = alpha;
		}

		readonly struct PortraitMotion
		{
			public readonly PortraitSlot Slot;
			public readonly Vector2 StartPosition;
			public readonly Vector2 EndPosition;
			public readonly float StartAlpha;
			public readonly float EndAlpha;

			public PortraitMotion(PortraitSlot slot, Vector2 startPosition, Vector2 endPosition, float startAlpha, float endAlpha)
			{
				Slot = slot;
				StartPosition = startPosition;
				EndPosition = endPosition;
				StartAlpha = startAlpha;
				EndAlpha = endAlpha;
			}
		}
	}

	static class BattlePortraitSpriteCache
	{
		static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();
		static readonly Dictionary<string, System.Threading.Tasks.Task<Sprite>> PendingLoads = new Dictionary<string, System.Threading.Tasks.Task<Sprite>>();
		static readonly HashSet<string> FailedAddresses = new HashSet<string>();

		public static System.Threading.Tasks.Task<Sprite> LoadAsync(string address)
		{
			if (Sprites.TryGetValue(address, out Sprite sprite))
				return System.Threading.Tasks.Task.FromResult(sprite);
			if (FailedAddresses.Contains(address))
				return System.Threading.Tasks.Task.FromResult<Sprite>(null);
			if (PendingLoads.TryGetValue(address, out System.Threading.Tasks.Task<Sprite> pendingLoad))
				return pendingLoad;

			System.Threading.Tasks.Task<Sprite> loadTask = LoadNewAsync(address);
			PendingLoads[address] = loadTask;
			return loadTask;
		}

		static async System.Threading.Tasks.Task<Sprite> LoadNewAsync(string address)
		{
			AsyncOperationHandle<Sprite> handle = default;
			try
			{
				handle = Addressables.LoadAssetAsync<Sprite>(address);
				await handle.Task;
				if (handle.Status == AsyncOperationStatus.Succeeded)
				{
					Sprites[address] = handle.Result;
					return handle.Result;
				}

				FailedAddresses.Add(address);
				Debug.LogWarning($"Failed to load battle portrait address '{address}'. The battle will continue without that portrait.");
				return null;
			}
			catch (Exception exception)
			{
				FailedAddresses.Add(address);
				Debug.LogWarning($"Battle portrait load failed for '{address}', but battle entry will continue. {exception.Message}");
				return null;
			}
			finally
			{
				PendingLoads.Remove(address);
				if (handle.IsValid() && handle.Status != AsyncOperationStatus.Succeeded)
					Addressables.Release(handle);
			}
		}
	}
}
