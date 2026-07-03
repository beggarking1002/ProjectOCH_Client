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
    private const string UiLabel = "UI";
    private const string BattleUiAddress = "BattleSceneUI";
    private const string DefaultRemoteLoadPath = "http://localhost/[BuildTarget]";
    private const string BattleUiPrefabPath = "Assets/@Resources/Prefab/UI/BattleSceneUI.prefab";

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
            || IsMissingAddressable(settings, BattleUiPrefabPath);
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

        group = settings.CreateGroup(
            UiGroupName,
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
}
