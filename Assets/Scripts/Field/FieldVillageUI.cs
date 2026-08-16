using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;
using App;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Field
{
	[DisallowMultipleComponent]
	public sealed class FieldVillageUI : MonoBehaviour
	{
		const string FieldVillageShopUiAddress = "FieldVillageShopUI";
		const string FieldVillageQuestUiAddress = "FieldVillageQuestUI";
		static readonly string[] PreviewArtworkAddresses =
		{
			"Village/village1", "Village/village2", "Village/village3", "Village/village4", "Village/village5",
			"Village/village6", "Village/village7", "Village/village8", "Village/village9", "Village/village10"
		};

		[SerializeField] Image townArtwork;
		[SerializeField] Button shopTabButton;
		[SerializeField] Button questTabButton;
		[SerializeField] Text villageNameText;
		[SerializeField] Text villageDescriptionText;
		[SerializeField] Text fameText;

		Canvas _canvas;
		AsyncOperationHandle<Sprite> _artworkHandle;
		bool _hasArtworkHandle;
		int _artworkLoadVersion;
		bool _isShopOpening;
		bool _isQuestOpening;
		string _currentVillageId;

		void Awake()
		{
			_canvas = GetComponent<Canvas>();
			SetCanvasVisible(false);
		}

		public void ShowVillage(FieldVillageDefinition village)
		{
			_currentVillageId = village.Id;
			SetCanvasVisible(false);
			if (villageNameText != null)
				villageNameText.text = village.Name;
			if (villageDescriptionText != null)
				villageDescriptionText.text = village.Description;

			if (string.IsNullOrWhiteSpace(village.ArtworkAddress))
			{
				SetCanvasVisible(true);
				return;
			}

			ShowArtwork(village.ArtworkAddress);
		}

		void OnEnable()
		{
			shopTabButton?.onClick.AddListener(OpenShop);
			questTabButton?.onClick.AddListener(OpenQuest);
			if (GameRoot.Instance != null)
			{
				GameRoot.Instance.Network.ExpeditionStateReceived += RenderFame;
				RenderFame(GameRoot.Instance.Network.LastExpeditionState);
			}
		}

		void OnDisable()
		{
			shopTabButton?.onClick.RemoveListener(OpenShop);
			questTabButton?.onClick.RemoveListener(OpenQuest);
			if (GameRoot.Instance != null)
				GameRoot.Instance.Network.ExpeditionStateReceived -= RenderFame;
			SetCanvasVisible(false);
			ReleaseArtwork();
		}

		void RenderFame(Protocol.S_EXPEDITION_STATE state)
		{
			if (fameText != null)
				fameText.text = $"명성  {state?.Fame ?? 0}";
		}

		async void OpenShop()
		{
			if (_isShopOpening)
				return;

			_isShopOpening = true;
			AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync(FieldVillageShopUiAddress);
			await handle.Task;
			_isShopOpening = false;
			if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
			{
				Debug.LogError($"Failed to load village shop UI: {FieldVillageShopUiAddress}");
				if (handle.IsValid())
					Addressables.Release(handle);
				return;
			}

			FieldVillageShopUI shopUi = handle.Result.GetComponent<FieldVillageShopUI>();
			if (shopUi == null)
			{
				Addressables.ReleaseInstance(handle.Result);
				return;
			}

			shopUi.Show(this, _currentVillageId);
		}

		async void OpenQuest()
		{
			if (_isQuestOpening)
				return;

			_isQuestOpening = true;
			AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync(FieldVillageQuestUiAddress);
			await handle.Task;
			_isQuestOpening = false;
			if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
			{
				Debug.LogError($"Failed to load village quest UI: {FieldVillageQuestUiAddress}");
				if (handle.IsValid())
					Addressables.Release(handle);
				return;
			}

			FieldVillageQuestUI questUi = handle.Result.GetComponent<FieldVillageQuestUI>();
			if (questUi == null)
			{
				Addressables.ReleaseInstance(handle.Result);
				return;
			}

			questUi.Show(this, _currentVillageId);
		}

		public void ShowRandomArtwork()
		{
			ShowArtwork(PreviewArtworkAddresses[Random.Range(0, PreviewArtworkAddresses.Length)]);
		}

		// The settlement table will call this with its assigned Addressables key.
		public async void ShowArtwork(string artworkAddress)
		{
			int loadVersion = ++_artworkLoadVersion;
			ReleaseArtwork();
			if (townArtwork == null || string.IsNullOrWhiteSpace(artworkAddress))
				return;

			// Never leave the previous village's artwork on screen while the next
			// Addressables sprite is loading. The window frame remains visible, and
			// the new image is enabled only after its own load succeeds.
			townArtwork.sprite = null;
			townArtwork.enabled = false;
			if (FieldVillageArtworkCache.TryGet(artworkAddress, out Sprite cachedArtwork))
			{
				ApplyArtwork(cachedArtwork);
				return;
			}

			AsyncOperationHandle<Sprite> handle = Addressables.LoadAssetAsync<Sprite>(artworkAddress);
			_artworkHandle = handle;
			_hasArtworkHandle = true;
			await handle.Task;

			if (this == null || isActiveAndEnabled == false || loadVersion != _artworkLoadVersion)
				return;

			if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
			{
				Debug.LogWarning($"Failed to load village artwork: {artworkAddress}");
				ReleaseArtwork();
				return;
			}

			ApplyArtwork(handle.Result);
		}

		void ApplyArtwork(Sprite artwork)
		{
			townArtwork.sprite = artwork;
			townArtwork.color = Color.white;
			townArtwork.preserveAspect = true;
			townArtwork.enabled = true;
			SetCanvasVisible(true);
		}

		void SetCanvasVisible(bool visible)
		{
			if (_canvas == null)
				_canvas = GetComponent<Canvas>();

			if (_canvas != null)
				_canvas.enabled = visible;
		}

		void ReleaseArtwork()
		{
			if (_hasArtworkHandle && _artworkHandle.IsValid())
				Addressables.Release(_artworkHandle);

			_artworkHandle = default;
			_hasArtworkHandle = false;
		}
	}

	// Keeps the authored village illustrations resident after the title-to-field
	// transition, so opening a village never waits for an Addressables fetch.
	public static class FieldVillageArtworkCache
	{
		static readonly string[] ArtworkAddresses =
		{
			"Village/eastgate",
			"Village/NorthWatch",
			"Village/RiverSide",
			"Village/SouthPort",
			"Village/WestField",
		};

		static readonly Dictionary<string, AsyncOperationHandle<Sprite>> Handles = new Dictionary<string, AsyncOperationHandle<Sprite>>();

		public static async Task PreloadAsync()
		{
			for (int index = 0; index < ArtworkAddresses.Length; index++)
			{
				string address = ArtworkAddresses[index];
				if (TryGet(address, out _))
					continue;

				if (Handles.TryGetValue(address, out AsyncOperationHandle<Sprite> existing))
				{
					await existing.Task;
					continue;
				}

				AsyncOperationHandle<Sprite> handle = Addressables.LoadAssetAsync<Sprite>(address);
				Handles.Add(address, handle);
				await handle.Task;
				if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null)
					continue;

				Debug.LogWarning($"Failed to preload village artwork: {address}");
				if (handle.IsValid())
					Addressables.Release(handle);
				Handles.Remove(address);
			}
		}

		public static bool TryGet(string address, out Sprite sprite)
		{
			sprite = null;
			return string.IsNullOrWhiteSpace(address) == false
				&& Handles.TryGetValue(address, out AsyncOperationHandle<Sprite> handle)
				&& handle.IsValid()
				&& handle.Status == AsyncOperationStatus.Succeeded
				&& (sprite = handle.Result) != null;
		}
	}
}
