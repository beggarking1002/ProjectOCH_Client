using System;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

internal static class AddressablesProjectSetup
{
    private const string RemoteGroupName = "Remote Content";
    private const string DefaultRemoteLoadPath = "http://localhost/[BuildTarget]";

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
            || remoteLoadPath == "<undefined>";
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
}
