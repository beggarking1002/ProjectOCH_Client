using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.UI;

namespace Field
{
	[DisallowMultipleComponent]
	public sealed class FieldVillageShopUI : MonoBehaviour
	{
		[SerializeField] Button backButton;

		FieldVillageUI _villageUi;

		public void Show(FieldVillageUI villageUi)
		{
			_villageUi = villageUi;
			backButton.onClick.AddListener(ReturnToVillage);
		}

		void ReturnToVillage()
		{
			backButton.onClick.RemoveListener(ReturnToVillage);
			if (_villageUi != null)
				_villageUi.gameObject.SetActive(true);

			Addressables.ReleaseInstance(gameObject);
		}
	}
}
