using System;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

internal static class AddressablesProjectSetup
{
    private const string RemoteGroupName = "Remote Content";
    private const string UiGroupName = "UI";
    private const string GameDataGroupName = "GameData";
    private const string SkillIconGroupName = "SkillIcon";
    private const string ItemIconGroupName = "ItemIcon";
    private const string UiLabel = "UI";
    private const string GameDataLabel = "GameData";
    private const string SkillIconLabel = "SkillIcon";
    private const string ItemIconLabel = "ItemIcon";
    private const string BattleUiAddress = "BattleSceneHUD";
    private const string StatIconAddress = "StatIcon";
    private const string HandCursorAddress = "Cursor_Hand";
    private const string AttackCursorAddress = "Cursor_Attack";
    private const string LootCursorAddress = "Cursor_Loot";
    private const string EmbeddedAxeMarkerPrefabAddress = "Equipment/SuenEmbeddedAxe";
    private const string ParvisMarkerPrefabAddress = "Equipment/SuenParvis";
    private const string DefaultRemoteLoadPath = "http://localhost/[BuildTarget]";
    private const string BattleUiPrefabPath = "Assets/@Resources/Prefab/UI/BattleSceneHUD.prefab";
    private const string StatIconPath = "Assets/@Resources/Art/UI/StatIcon.png";
    private const string HandCursorPath = "Assets/@Resources/Art/UI/Cursor/Hand.png";
    private const string AttackCursorPath = "Assets/@Resources/Art/UI/Cursor/Attack.png";
    private const string LootCursorPath = "Assets/@Resources/Art/UI/Cursor/Loot.png";
    private const string EmbeddedAxeMarkerPrefabPath = "Assets/@Resources/Prefab/Equipment/SuenEmbeddedAxe.prefab";
    private const string ParvisMarkerPrefabPath = "Assets/@Resources/Prefab/Equipment/SuenParvis.prefab";
    private static readonly string[] GameDataCsvPaths =
    {
        "Assets/GameData/ClassKey.csv",
        "Assets/GameData/PawnTemplate.csv",
        "Assets/GameData/BattleSkill.csv",
        "Assets/GameData/BattleSkillEffect.csv",
        "Assets/GameData/BattleSkillEffectParam.csv",
        "Assets/GameData/BattleSkillView.csv",
        "Assets/GameData/DisplayText.csv",
        "Assets/GameData/EnumDef.csv",
        "Assets/GameData/Village.csv",
		"Assets/GameData/Item.csv",
		"Assets/GameData/ItemIcon.csv",
		"Assets/GameData/VillageShopStock.csv",
		"Assets/GameData/VillageTrade.csv",
		"Assets/GameData/EconomyConfig.csv",
    };
    private static readonly SkillIconAsset[] SkillIconAssets =
    {
        new SkillIconAsset("Assets/@Resources/Art/SkillIcon/Beige/Beige_Ice_Passive.png", "icon_beige_ice_passive"),
        new SkillIconAsset("Assets/@Resources/Art/SkillIcon/Beige/Beige_Ice_Skill1.png", "icon_beige_ice_skill1"),
        new SkillIconAsset("Assets/@Resources/Art/SkillIcon/Beige/Beige_Ice_Skill2.png", "icon_beige_ice_skill2"),
        new SkillIconAsset("Assets/@Resources/Art/SkillIcon/Beige/Beige_Ice_Skill3.png", "icon_beige_ice_skill3"),
        new SkillIconAsset("Assets/@Resources/Art/SkillIcon/Beige/Beige_Ice_Skill4.png", "icon_beige_ice_skill4"),
        new SkillIconAsset("Assets/@Resources/Art/SkillIcon/Beige/Beige_Ice_Ulti.png", "icon_beige_ice_ulti"),
        new SkillIconAsset("Assets/@Resources/Art/SkillIcon/Beige/Beige_Ice_Sub.png", "icon_beige_ice_sub"),
    };
    private static readonly SkillIconAsset[] VillageArtworkAssets =
    {
        new SkillIconAsset("Assets/@Resources/Art/Village/eastgate.png", "Village/eastgate"),
        new SkillIconAsset("Assets/@Resources/Art/Village/NorthWatch.png", "Village/NorthWatch"),
        new SkillIconAsset("Assets/@Resources/Art/Village/RiverSide.png", "Village/RiverSide"),
        new SkillIconAsset("Assets/@Resources/Art/Village/SouthPort.png", "Village/SouthPort"),
        new SkillIconAsset("Assets/@Resources/Art/Village/WestField.png", "Village/WestField"),
    };
    private static readonly SkillIconAsset[] ItemIconSheets =
    {
        new SkillIconAsset("Assets/@Resources/Art/Item/Item1.png", "Item/Item1"),
        new SkillIconAsset("Assets/@Resources/Art/Item/Item2.png", "Item/Item2"),
        new SkillIconAsset("Assets/@Resources/Art/Item/Item3.png", "Item/Item3"),
    };

    [InitializeOnLoadMethod]
    private static void InitializeOnFirstInstall()
    {
        EditorApplication.delayCall += () =>
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null || NeedsInitialization(settings))
            {
                Initialize();
            }
        };
    }

    [MenuItem("Tools/Project OCH/Addressables/Initialize")]
    private static void Initialize()
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
        if (settings == null)
        {
            Debug.LogError("Failed to create Addressables settings.");
            return;
        }

        if (settings.FindGroup(RemoteGroupName) == null)
        {
            AddressableAssetGroup remoteGroup = settings.CreateGroup(
                RemoteGroupName,
                false,
                false,
                true,
                null,
                typeof(BundledAssetGroupSchema),
                typeof(ContentUpdateGroupSchema));

            ConfigureRemoteGroup(settings, remoteGroup);
        }

        ConfigureDefaultRemoteLoadPath(settings);
        ConfigureRemoteCatalogPaths(settings);
        RegisterUiAddressable(settings, BattleUiPrefabPath, BattleUiAddress);
        RegisterUiAddressable(settings, StatIconPath, StatIconAddress);
        RegisterUiAddressable(settings, HandCursorPath, HandCursorAddress);
        RegisterUiAddressable(settings, AttackCursorPath, AttackCursorAddress);
        RegisterUiAddressable(settings, LootCursorPath, LootCursorAddress);
        RegisterUiAddressable(settings, EmbeddedAxeMarkerPrefabPath, EmbeddedAxeMarkerPrefabAddress);
        RegisterUiAddressable(settings, ParvisMarkerPrefabPath, ParvisMarkerPrefabAddress);
        RegisterGameDataAddressables(settings);
        RegisterSkillIconAddressables(settings);
		RegisterItemIconAddressables(settings);
		RegisterVillageArtworkAddressables(settings);

        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        Debug.Log("Addressables initialized with local and remote content groups.");
    }

    private static void ConfigureRemoteGroup(
        AddressableAssetSettings settings,
        AddressableAssetGroup group)
    {
        BundledAssetGroupSchema bundleSchema = group.GetSchema<BundledAssetGroupSchema>();
        bundleSchema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteBuildPath);
        bundleSchema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteLoadPath);
        bundleSchema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogether;
        bundleSchema.IncludeInBuild = true;

        ContentUpdateGroupSchema updateSchema = group.GetSchema<ContentUpdateGroupSchema>();
        updateSchema.StaticContent = false;

        EditorUtility.SetDirty(bundleSchema);
        EditorUtility.SetDirty(updateSchema);
    }

    private static bool NeedsInitialization(AddressableAssetSettings settings)
    {
        string remoteLoadPath = settings.profileSettings.GetValueByName(
            settings.activeProfileId,
            AddressableAssetSettings.kRemoteLoadPath);

        return settings.FindGroup(RemoteGroupName) == null
            || string.IsNullOrWhiteSpace(remoteLoadPath)
            || remoteLoadPath == "<undefined>"
            || IsMissingAddressable(settings, BattleUiPrefabPath)
            || IsMissingAddressable(settings, StatIconPath)
            || IsMissingAddressable(settings, HandCursorPath)
            || IsMissingAddressable(settings, AttackCursorPath)
            || IsMissingAddressable(settings, LootCursorPath)
            || IsMissingAddressable(settings, EmbeddedAxeMarkerPrefabPath)
            || IsMissingAddressable(settings, ParvisMarkerPrefabPath)
			|| IsMissingAnyAddressable(settings, GameDataCsvPaths)
			|| IsMissingAnySkillIconAddressable(settings)
			|| IsMissingAnyItemIconAddressable(settings)
			|| IsMissingAnyVillageArtworkAddressable(settings);
    }

    private static void ConfigureDefaultRemoteLoadPath(AddressableAssetSettings settings)
    {
        string remoteLoadPath = settings.profileSettings.GetValueByName(
            settings.activeProfileId,
            AddressableAssetSettings.kRemoteLoadPath);

        if (string.IsNullOrWhiteSpace(remoteLoadPath) || remoteLoadPath == "<undefined>")
        {
            settings.profileSettings.SetValue(
                settings.activeProfileId,
                AddressableAssetSettings.kRemoteLoadPath,
                DefaultRemoteLoadPath);
        }
    }

    private static void ConfigureRemoteCatalogPaths(AddressableAssetSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.RemoteCatalogBuildPath.Id))
        {
            settings.RemoteCatalogBuildPath.SetVariableByName(
                settings,
                AddressableAssetSettings.kRemoteBuildPath);
        }

        if (string.IsNullOrWhiteSpace(settings.RemoteCatalogLoadPath.Id))
        {
            settings.RemoteCatalogLoadPath.SetVariableByName(
                settings,
                AddressableAssetSettings.kRemoteLoadPath);
        }
    }

    private static void RegisterUiAddressable(
        AddressableAssetSettings settings,
        string assetPath,
        string address)
    {
        string guid = AssetDatabase.AssetPathToGUID(assetPath);
        if (string.IsNullOrWhiteSpace(guid))
        {
            Debug.LogWarning($"Addressable asset not found: {assetPath}");
            return;
        }

        AddressableAssetGroup group = GetOrCreateUiGroup(settings);
        if (group == null)
        {
            Debug.LogWarning($"Cannot register UI addressable without a group: {assetPath}");
            return;
        }

        AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, group, false, false);
        entry.address = address;
        settings.AddLabel(UiLabel, false);
        entry.SetLabel(UiLabel, true, true, false);
        EditorUtility.SetDirty(group);
    }

    private static AddressableAssetGroup GetOrCreateUiGroup(AddressableAssetSettings settings)
    {
        AddressableAssetGroup group = settings.FindGroup(UiGroupName);
        if (group != null)
            return group;

        return GetOrCreateLocalGroup(settings, UiGroupName);
    }

    private static void RegisterSkillIconAddressables(AddressableAssetSettings settings)
    {
        AddressableAssetGroup group = GetOrCreateLocalGroup(settings, SkillIconGroupName);
        if (group == null)
        {
            Debug.LogWarning("Cannot register skill icon addressables without a group.");
            return;
        }

        settings.AddLabel(SkillIconLabel, false);
        foreach (SkillIconAsset icon in SkillIconAssets)
        {
            string guid = AssetDatabase.AssetPathToGUID(icon.Path);
            if (string.IsNullOrWhiteSpace(guid))
            {
                Debug.LogWarning($"Skill icon asset not found: {icon.Path}");
                continue;
            }

            EnsureSpriteImporter(icon.Path);
            AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, group, false, false);
            entry.address = icon.Address;
            entry.SetLabel(SkillIconLabel, true, true, false);
        }

        EditorUtility.SetDirty(group);
    }

	private static void RegisterItemIconAddressables(AddressableAssetSettings settings)
	{
		AddressableAssetGroup group = GetOrCreateLocalGroup(settings, ItemIconGroupName);
		if (group == null)
		{
			Debug.LogWarning("Cannot register item icon addressables without a group.");
			return;
		}

		settings.AddLabel(ItemIconLabel, false);
		foreach (SkillIconAsset sheet in ItemIconSheets)
		{
			string guid = AssetDatabase.AssetPathToGUID(sheet.Path);
			if (string.IsNullOrWhiteSpace(guid))
			{
				Debug.LogWarning($"Item icon sheet not found: {sheet.Path}");
				continue;
			}

			AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, group, false, false);
			entry.address = sheet.Address;
			entry.SetLabel(ItemIconLabel, true, true, false);
		}

		EditorUtility.SetDirty(group);
	}

	private static void RegisterVillageArtworkAddressables(AddressableAssetSettings settings)
	{
		foreach (SkillIconAsset artwork in VillageArtworkAssets)
		{
			EnsureSpriteImporter(artwork.Path);
			RegisterUiAddressable(settings, artwork.Path, artwork.Address);
		}
	}

    private static void RegisterGameDataAddressables(AddressableAssetSettings settings)
    {
        AddressableAssetGroup group = GetOrCreateLocalGroup(settings, GameDataGroupName);
        if (group == null)
        {
            Debug.LogWarning("Cannot register GameData addressables without a group.");
            return;
        }

        settings.AddLabel(GameDataLabel, false);
        foreach (string assetPath in GameDataCsvPaths)
        {
            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrWhiteSpace(guid))
            {
                Debug.LogWarning($"GameData asset not found: {assetPath}");
                continue;
            }

            AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, group, false, false);
            entry.address = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            entry.SetLabel(GameDataLabel, true, true, false);
        }

        EditorUtility.SetDirty(group);
    }

    private static void EnsureSpriteImporter(string assetPath)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
            return;

        bool changed = false;
        if (importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            changed = true;
        }

        if (importer.spriteImportMode != SpriteImportMode.Single)
        {
            importer.spriteImportMode = SpriteImportMode.Single;
            changed = true;
        }

        if (importer.alphaIsTransparency == false)
        {
            importer.alphaIsTransparency = true;
            changed = true;
        }

        if (changed)
            importer.SaveAndReimport();
    }

    private static AddressableAssetGroup GetOrCreateLocalGroup(AddressableAssetSettings settings, string groupName)
    {
        AddressableAssetGroup group = settings.FindGroup(groupName);
        if (group != null)
            return group;

        group = settings.CreateGroup(
            groupName,
            false,
            false,
            true,
            null,
            typeof(BundledAssetGroupSchema),
            typeof(ContentUpdateGroupSchema));

        BundledAssetGroupSchema bundleSchema = group.GetSchema<BundledAssetGroupSchema>();
        if (bundleSchema != null)
        {
            bundleSchema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kLocalBuildPath);
            bundleSchema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kLocalLoadPath);
            bundleSchema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogether;
            bundleSchema.IncludeInBuild = true;
            EditorUtility.SetDirty(bundleSchema);
        }

        ContentUpdateGroupSchema updateSchema = group.GetSchema<ContentUpdateGroupSchema>();
        if (updateSchema != null)
        {
            updateSchema.StaticContent = true;
            EditorUtility.SetDirty(updateSchema);
        }

        return group;
    }

    private static bool IsMissingAddressable(AddressableAssetSettings settings, string assetPath)
    {
        string guid = AssetDatabase.AssetPathToGUID(assetPath);
        return string.IsNullOrWhiteSpace(guid) == false
            && settings.FindAssetEntry(guid) == null;
    }

    private static bool IsMissingAnyAddressable(AddressableAssetSettings settings, string[] assetPaths)
    {
        foreach (string assetPath in assetPaths)
        {
            if (IsMissingAddressable(settings, assetPath))
                return true;
        }

        return false;
    }

    private static bool IsMissingAnySkillIconAddressable(AddressableAssetSettings settings)
    {
        foreach (SkillIconAsset icon in SkillIconAssets)
        {
            if (IsMissingAddressable(settings, icon.Path))
                return true;
        }

        return false;
    }

	private static bool IsMissingAnyItemIconAddressable(AddressableAssetSettings settings)
	{
		foreach (SkillIconAsset sheet in ItemIconSheets)
		{
			if (IsMissingAddressable(settings, sheet.Path))
				return true;
		}

		return false;
	}

	private static bool IsMissingAnyVillageArtworkAddressable(AddressableAssetSettings settings)
	{
		foreach (SkillIconAsset artwork in VillageArtworkAssets)
		{
			if (IsMissingAddressable(settings, artwork.Path))
				return true;
		}

		return false;
	}

    private readonly struct SkillIconAsset
    {
        public readonly string Path;
        public readonly string Address;

        public SkillIconAsset(string path, string address)
        {
            Path = path;
            Address = address;
        }
    }
}
