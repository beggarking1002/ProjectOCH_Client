using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Field
{
	public readonly struct FieldItemDefinition
	{
		public readonly string Id;
		public readonly string Type;
		public readonly string Name;
		public readonly int SatietyDelta;
		public readonly int HappinessDelta;
		public readonly int ThirstDelta;
		public readonly int ShelfLifeDays;

		public bool IsFood => string.Equals(Type, "FOOD", StringComparison.OrdinalIgnoreCase);
		public bool IsDrink => string.Equals(Type, "DRINK", StringComparison.OrdinalIgnoreCase);

		public FieldItemDefinition(string id, string type, string name, int satietyDelta, int happinessDelta, int thirstDelta, int shelfLifeDays)
		{
			Id = id;
			Type = type;
			Name = name;
			SatietyDelta = satietyDelta;
			HappinessDelta = happinessDelta;
			ThirstDelta = thirstDelta;
			ShelfLifeDays = shelfLifeDays;
		}
	}

	public readonly struct FieldItemIconDefinition
	{
		public readonly string SheetAddress;
		public readonly string SpriteName;

		public FieldItemIconDefinition(string sheetAddress, string spriteName)
		{
			SheetAddress = sheetAddress;
			SpriteName = spriteName;
		}
	}

	public sealed class FieldEconomyRepository
	{
		const string ItemTableAddress = "Item";
		const string ItemIconTableAddress = "ItemIcon";
		readonly Dictionary<string, FieldItemDefinition> _items = new Dictionary<string, FieldItemDefinition>(StringComparer.OrdinalIgnoreCase);
		readonly Dictionary<string, FieldItemIconDefinition> _itemIcons = new Dictionary<string, FieldItemIconDefinition>(StringComparer.OrdinalIgnoreCase);
		static readonly Dictionary<string, Sprite> LoadedSprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
		static readonly List<AsyncOperationHandle<Sprite>> LoadedSpriteHandles = new List<AsyncOperationHandle<Sprite>>();

		public static async Task<FieldEconomyRepository> LoadAsync()
		{
			FieldEconomyRepository repository = new FieldEconomyRepository();
			string[] tables = await Task.WhenAll(LoadTextAsync(ItemTableAddress), LoadTextAsync(ItemIconTableAddress));
			repository.ParseItems(tables[0]);
			repository.ParseItemIcons(tables[1]);
			return repository;
		}

		public bool TryGetItem(string itemId, out FieldItemDefinition item)
		{
			return _items.TryGetValue(itemId ?? string.Empty, out item);
		}

		public async Task<Sprite> LoadItemIconAsync(string itemId)
		{
			if (_itemIcons.TryGetValue(itemId ?? string.Empty, out FieldItemIconDefinition icon) == false ||
				string.IsNullOrWhiteSpace(icon.SheetAddress) || string.IsNullOrWhiteSpace(icon.SpriteName))
				return null;

			string address = $"{icon.SheetAddress}[{icon.SpriteName}]";
			if (LoadedSprites.TryGetValue(address, out Sprite cached))
				return cached;

			AsyncOperationHandle<Sprite> handle = Addressables.LoadAssetAsync<Sprite>(address);
			await handle.Task;
			if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
			{
				Debug.LogWarning($"Failed to load item icon: {address}");
				if (handle.IsValid())
					Addressables.Release(handle);
				return null;
			}

			LoadedSprites[address] = handle.Result;
			LoadedSpriteHandles.Add(handle);
			return handle.Result;
		}

		void ParseItems(string csv)
		{
			foreach (Dictionary<string, string> row in ReadRows(csv))
			{
				if (TryGet(row, "ItemId", out string itemId) == false || string.IsNullOrWhiteSpace(itemId))
					continue;
				TryGet(row, "ItemType", out string itemType);
				TryGet(row, "DisplayName", out string displayName);
				_items[itemId] = new FieldItemDefinition(
					itemId,
					itemType,
					displayName,
					ParseInt(row, "SatietyDelta"),
					ParseInt(row, "HappinessDelta"),
					ParseInt(row, "ThirstDelta"),
					ParseInt(row, "ShelfLifeDays", -1));
			}
		}

		void ParseItemIcons(string csv)
		{
			foreach (Dictionary<string, string> row in ReadRows(csv))
			{
				if (TryGet(row, "ItemId", out string itemId) == false || string.IsNullOrWhiteSpace(itemId))
					continue;
				TryGet(row, "SheetAddress", out string sheetAddress);
				TryGet(row, "SpriteName", out string spriteName);
				_itemIcons[itemId] = new FieldItemIconDefinition(sheetAddress, spriteName);
			}
		}

		static int ParseInt(IReadOnlyDictionary<string, string> row, string key, int fallback = 0)
		{
			return row.TryGetValue(key, out string value) && int.TryParse(value, out int parsed) ? parsed : fallback;
		}

		static async Task<string> LoadTextAsync(string address)
		{
			AsyncOperationHandle<TextAsset> handle = Addressables.LoadAssetAsync<TextAsset>(address);
			await handle.Task;
			if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
			{
				Debug.LogWarning($"Failed to load field economy data: {address}");
				if (handle.IsValid())
					Addressables.Release(handle);
				return string.Empty;
			}

			string text = handle.Result.text;
			Addressables.Release(handle);
			return text;
		}

		static IEnumerable<Dictionary<string, string>> ReadRows(string csv)
		{
			if (string.IsNullOrWhiteSpace(csv))
				yield break;

			string[] lines = csv.Replace("\r", string.Empty).Split('\n');
			if (lines.Length < 3)
				yield break;

			string[] headers = lines[0].Split(',');
			for (int lineIndex = 2; lineIndex < lines.Length; lineIndex++)
			{
				if (string.IsNullOrWhiteSpace(lines[lineIndex]))
					continue;

				string[] values = lines[lineIndex].Split(',');
				Dictionary<string, string> row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				for (int column = 0; column < headers.Length; column++)
					row[headers[column].Trim()] = column < values.Length ? values[column].Trim() : string.Empty;
				yield return row;
			}
		}

		static bool TryGet(IReadOnlyDictionary<string, string> row, string key, out string value)
		{
			return row.TryGetValue(key, out value);
		}
	}
}
