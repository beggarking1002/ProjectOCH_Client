using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;

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

		AsyncOperationHandle<Sprite> _artworkHandle;
		bool _hasArtworkHandle;
		int _artworkLoadVersion;
		bool _isShopOpening;
		bool _isQuestOpening;
		bool _hasAcceptedQuest;

		public void ShowVillage(FieldVillageDefinition village)
		{
			if (villageNameText != null)
				villageNameText.text = village.Name;
			if (villageDescriptionText != null)
				villageDescriptionText.text = village.Description;

			if (string.IsNullOrWhiteSpace(village.ArtworkAddress) == false)
				ShowArtwork(village.ArtworkAddress);
		}

		void OnEnable()
		{
			shopTabButton?.onClick.AddListener(OpenShop);
			questTabButton?.onClick.AddListener(OpenQuest);
			ShowRandomArtwork();
		}

		void OnDisable()
		{
			shopTabButton?.onClick.RemoveListener(OpenShop);
			questTabButton?.onClick.RemoveListener(OpenQuest);
			ReleaseArtwork();
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

			shopUi.Show(this);
			gameObject.SetActive(false);
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

			questUi.Show(this, _hasAcceptedQuest);
			gameObject.SetActive(false);
		}

		public void MarkQuestAccepted()
		{
			_hasAcceptedQuest = true;
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

			townArtwork.sprite = handle.Result;
			townArtwork.color = Color.white;
			townArtwork.preserveAspect = true;
		}

		void ReleaseArtwork()
		{
			if (_hasArtworkHandle && _artworkHandle.IsValid())
				Addressables.Release(_artworkHandle);

			_artworkHandle = default;
			_hasArtworkHandle = false;
		}
	}
}
