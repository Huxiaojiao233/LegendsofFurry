using System.Linq;
using LegendsOfFurry.Content.Contracts;
using LegendsOfFurry.Content.Runtime;
using UnityEngine;

/// <summary>Persists the selected authored class by stable content ID instead of a compiled enum.</summary>
public static class GameSession
{
    private const string ClassIdKey = "LegendsOfFurry.SelectedClassId";
    private const string ContentFingerprintKey = "LegendsOfFurry.SelectedContentFingerprint";
    private static bool hasRuntimeSelection;
    private static string selectedClassId = string.Empty;

    public static string SelectedClassId
    {
        get
        {
            if (!hasRuntimeSelection) LoadSelection();
            return selectedClassId;
        }
    }
    public static string SelectedContentFingerprint => PlayerPrefs.GetString(ContentFingerprintKey, string.Empty);

    public static void SelectClass(string classId)
    {
        selectedClassId = ContentId.Require(classId, nameof(classId));
        hasRuntimeSelection = true;
        PlayerPrefs.SetString(ClassIdKey, selectedClassId);
        PlayerPrefs.SetString(ContentFingerprintKey, ContentRuntime.ContentFingerprint);
        PlayerPrefs.Save();
    }

    public static void ClearSelectedClass()
    {
        hasRuntimeSelection = false;
        selectedClassId = string.Empty;
        PlayerPrefs.DeleteKey(ClassIdKey);
        PlayerPrefs.Save();
    }

    private static void LoadSelection()
    {
        hasRuntimeSelection = true;
        string persisted = PlayerPrefs.GetString(ClassIdKey, string.Empty);
        if (ContentId.IsValid(persisted) && IsAvailable(persisted))
        {
            selectedClassId = persisted;
            PlayerPrefs.SetString(ContentFingerprintKey, ContentRuntime.ContentFingerprint);
            PlayerPrefs.Save();
            return;
        }

        ClassProfileDefinition[] profiles = GetProfiles();
        selectedClassId = profiles.FirstOrDefault()?.ClassId ?? string.Empty;
    }

    private static bool IsAvailable(string classId) => ContentRuntime.IsLoaded &&
        ContentRuntime.Registry.TryGetClassProfile(classId, out _);

    private static ClassProfileDefinition[] GetProfiles() => ContentRuntime.IsLoaded
        ? ContentRuntime.Registry.ClassProfiles.Where(item => item.Enabled)
            .OrderBy(item => item.SortOrder).ThenBy(item => item.ClassId).ToArray()
        : System.Array.Empty<ClassProfileDefinition>();
}
