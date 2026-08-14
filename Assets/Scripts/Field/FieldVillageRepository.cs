using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Field
{
	public readonly struct FieldVillageDefinition
	{
		public readonly string Id;
		public readonly string Name;
		public readonly string Description;
		public readonly string ArtworkAddress;

		public FieldVillageDefinition(string id, string name, string description, string artworkAddress)
		{
			Id = id;
			Name = name;
			Description = description;
			ArtworkAddress = artworkAddress;
		}
	}

	public sealed class FieldVillageRepository
	{
		const string VillageTableAddress = "Village";
		readonly Dictionary<string, FieldVillageDefinition> _villages = new Dictionary<string, FieldVillageDefinition>(StringComparer.OrdinalIgnoreCase);

		public static async Task<FieldVillageRepository> LoadAsync()
		{
			string villageCsv = await LoadTextAsync(VillageTableAddress);
			FieldVillageRepository repository = new FieldVillageRepository();
			repository.ParseVillages(villageCsv);
			return repository;
		}

		public bool TryGetVillage(string villageId, out FieldVillageDefinition village)
		{
			village = default;
			return string.IsNullOrWhiteSpace(villageId) == false && _villages.TryGetValue(villageId, out village);
		}

		public bool TryGetFirstVillage(out FieldVillageDefinition village)
		{
			foreach (FieldVillageDefinition value in _villages.Values)
			{
				village = value;
				return true;
			}

			village = default;
			return false;
		}

		void ParseVillages(string csv)
		{
			foreach (Dictionary<string, string> row in ReadRows(csv))
			{
				if (TryGet(row, "VillageId", out string id) == false || string.IsNullOrWhiteSpace(id))
					continue;

				TryGet(row, "Name", out string name);
				TryGet(row, "Description", out string description);
				TryGet(row, "ArtworkAddress", out string artworkAddress);
				_villages[id] = new FieldVillageDefinition(id, name, description, artworkAddress);
			}
		}

		static async Task<string> LoadTextAsync(string address)
		{
			AsyncOperationHandle<TextAsset> handle = Addressables.LoadAssetAsync<TextAsset>(address);
			await handle.Task;
			if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
			{
				Debug.LogWarning($"Failed to load field village data: {address}");
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
