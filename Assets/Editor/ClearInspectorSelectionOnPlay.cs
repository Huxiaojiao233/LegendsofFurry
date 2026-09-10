#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Unity 6 在 Domain Reload 时若 Inspector 正显示 Image/Button/RectTransform，
/// ugui 的 ImageEditor 会访问空 serializedObject。进 Play 和脚本编译都会重载程序集。
/// 重载前解开锁定的 Inspector 并清掉这类选中；编辑模式编译后再选回去。
/// </summary>
[InitializeOnLoad]
internal static class ClearUiInspectorBeforeDomainReload
{
    private const string ReselectKey = "LOF.UiInspectorReselect";

    static ClearUiInspectorBeforeDomainReload()
    {
        AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.delayCall += TryRestoreSelection;
    }

    [InitializeOnEnterPlayMode]
    private static void OnEnterPlayMode(EnterPlayModeOptions options)
    {
        ReleaseUiInspectors(false);
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode)
            ReleaseUiInspectors(false);
    }

    private static void OnBeforeAssemblyReload()
    {
        bool enteringOrLeavingPlay = EditorApplication.isPlayingOrWillChangePlaymode;
        ReleaseUiInspectors(!enteringOrLeavingPlay);
    }

    private static void ReleaseUiInspectors(bool storeForRestore)
    {
        UnityEngine.Object[] selected = Selection.objects;
        bool fragile = HasFragileInspector();
        if (!fragile)
            return;

        if (storeForRestore && selected != null && selected.Length > 0)
        {
            GlobalObjectId[] ids = new GlobalObjectId[selected.Length];
            GlobalObjectId.GetGlobalObjectIdsSlow(selected, ids);
            string[] parts = new string[ids.Length];
            for (int i = 0; i < ids.Length; i++)
                parts[i] = ids[i].ToString();
            SessionState.SetString(ReselectKey, string.Join(";", parts));
        }
        else
            SessionState.EraseString(ReselectKey);

        Selection.objects = Array.Empty<UnityEngine.Object>();
        UnlockInspectorWindows();
        ActiveEditorTracker.sharedTracker.isLocked = false;
        ActiveEditorTracker.sharedTracker.ForceRebuild();
    }

    private static bool HasFragileInspector()
    {
        UnityEngine.Object[] selected = Selection.objects;
        if (selected != null)
        {
            for (int i = 0; i < selected.Length; i++)
            {
                if (IsFragileInspectorTarget(selected[i]))
                    return true;
            }
        }

        return InspectorsShowFragileTargets();
    }

    private static bool IsFragileInspectorTarget(UnityEngine.Object obj)
    {
        if (obj == null) return true;
        GameObject go = obj as GameObject;
        if (obj is Component component)
            go = component.gameObject;
        if (go == null) return false;
        return go.GetComponent<RectTransform>() != null ||
               go.GetComponent<Graphic>() != null ||
               go.GetComponent<Selectable>() != null;
    }

    private static bool InspectorsShowFragileTargets()
    {
        ActiveEditorTracker[] trackers = CollectInspectorTrackers();
        for (int t = 0; t < trackers.Length; t++)
        {
            Editor[] editors = trackers[t].activeEditors;
            if (editors == null) continue;
            for (int i = 0; i < editors.Length; i++)
            {
                Editor editor = editors[i];
                if (editor == null || IsFragileInspectorTarget(editor.target))
                    return true;
            }
        }

        return false;
    }

    private static void UnlockInspectorWindows()
    {
        ActiveEditorTracker[] trackers = CollectInspectorTrackers();
        for (int i = 0; i < trackers.Length; i++)
        {
            trackers[i].isLocked = false;
            trackers[i].ForceRebuild();
        }
    }

    private static ActiveEditorTracker[] CollectInspectorTrackers()
    {
        List<ActiveEditorTracker> trackers = new List<ActiveEditorTracker> { ActiveEditorTracker.sharedTracker };
        Type inspectorType = typeof(Editor).Assembly.GetType("UnityEditor.InspectorWindow");
        if (inspectorType == null) return trackers.ToArray();
        PropertyInfo trackerProperty = inspectorType.GetProperty(
            "tracker", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (trackerProperty == null) return trackers.ToArray();
        UnityEngine.Object[] windows = Resources.FindObjectsOfTypeAll(inspectorType);
        for (int i = 0; i < windows.Length; i++)
        {
            if (trackerProperty.GetValue(windows[i]) is ActiveEditorTracker tracker && !trackers.Contains(tracker))
                trackers.Add(tracker);
        }

        return trackers.ToArray();
    }

    private static void TryRestoreSelection()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        string stored = SessionState.GetString(ReselectKey, string.Empty);
        if (string.IsNullOrEmpty(stored))
            return;
        SessionState.EraseString(ReselectKey);
        string[] parts = stored.Split(';');
        List<UnityEngine.Object> restored = new List<UnityEngine.Object>(parts.Length);
        for (int i = 0; i < parts.Length; i++)
        {
            if (!GlobalObjectId.TryParse(parts[i], out GlobalObjectId id))
                continue;
            UnityEngine.Object obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id);
            if (obj != null)
                restored.Add(obj);
        }

        if (restored.Count > 0)
            Selection.objects = restored.ToArray();
    }
}
#endif
