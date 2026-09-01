using System;
using System.Collections.Generic;
using System.Linq;
using LegendsOfFurry.Content.Contracts;
using LegendsOfFurry.Content.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class ClassSelectionController : MonoBehaviour
{
    [Serializable]
    private sealed class ClassCardView
    {
        public string classId;
        public GameObject root;
        public Button button;
        public Image artwork;
        public TMP_Text className;
        public TMP_Text description;
    }

    [Header("Scene-authored class cards")]
    [SerializeField] private List<ClassCardView> classCards = new();

    private void Awake()
    {
        RefreshClassCards();
    }

    /// <summary>
    /// Reads the existing content package and applies it to the uGUI objects that
    /// are already present in S_ClassSelect. No UI objects are created at runtime.
    /// </summary>
    private void RefreshClassCards()
    {
        if (!ContentRuntime.IsLoaded)
        {
            throw new InvalidOperationException(
                $"职业选择无法读取数据库内容包：{ContentRuntime.LoadError}");
        }

        ClassProfileDefinition[] orderedProfiles = ContentRuntime.Registry.ClassProfiles
                     .Where(item => item.Enabled)
                     .OrderBy(item => item.SortOrder).ThenBy(item => item.ClassId).ToArray();
        EnsureCardCapacity(orderedProfiles);
        Dictionary<string, ClassProfileDefinition> profiles = new(StringComparer.Ordinal);
        foreach (ClassProfileDefinition profile in orderedProfiles)
        {
            profiles[profile.ClassId] = profile;
        }

        foreach (ClassCardView card in classCards)
        {
            if (card.root == null || card.button == null)
            {
                Debug.LogWarning("S_ClassSelect 中存在未完整绑定的职业卡。", this);
                continue;
            }

            if (!profiles.TryGetValue(card.classId, out ClassProfileDefinition profile))
            {
                card.root.SetActive(false);
                continue;
            }

            card.root.SetActive(true);
            card.button.onClick.RemoveAllListeners();
            string selectedClassId = card.classId;
            card.button.onClick.AddListener(() => Select(selectedClassId));

            if (card.className != null)
            {
                card.className.text = profile.DisplayName;
            }

            if (card.description != null)
            {
                card.description.text = profile.Description;
            }

            if (card.artwork != null)
            {
                Sprite sprite = RuntimeCardAdapter.LoadManagedSprite(
                    profile.GetTraitString("artwork_key"), "class_artwork");
                if (sprite != null)
                {
                    card.artwork.sprite = sprite;
                    card.artwork.color = Color.white;
                }
                else
                {
                    Debug.LogWarning(
                        $"未找到职业图片资源：{profile.GetTraitString("artwork_key")}", this);
                }
            }
        }
    }

    private void EnsureCardCapacity(IReadOnlyList<ClassProfileDefinition> profiles)
    {
        if (classCards.Count == 0) return;
        HashSet<string> assigned = classCards.Select(item => item.classId).ToHashSet(StringComparer.Ordinal);
        ClassCardView template = classCards[0];
        foreach (ClassProfileDefinition profile in profiles)
        {
            if (assigned.Contains(profile.ClassId)) continue;
            GameObject clone = Instantiate(template.root, template.root.transform.parent);
            clone.name = "ClassCard_" + profile.ClassId;
            ClassCardView created = new ClassCardView
            {
                classId = profile.ClassId,
                root = clone,
                button = ResolveCloneComponent(template.root, template.button, clone),
                artwork = ResolveCloneComponent(template.root, template.artwork, clone),
                className = ResolveCloneComponent(template.root, template.className, clone),
                description = ResolveCloneComponent(template.root, template.description, clone)
            };
            classCards.Add(created);
            assigned.Add(profile.ClassId);
        }
    }

    private static T ResolveCloneComponent<T>(GameObject templateRoot, T templateComponent, GameObject cloneRoot)
        where T : Component
    {
        if (templateComponent == null) return null;
        string path = RelativePath(templateRoot.transform, templateComponent.transform);
        Transform clone = string.IsNullOrEmpty(path) ? cloneRoot.transform : cloneRoot.transform.Find(path);
        return clone?.GetComponent<T>();
    }

    private static string RelativePath(Transform root, Transform target)
    {
        if (root == target) return string.Empty;
        List<string> parts = new List<string>();
        for (Transform current = target; current != null && current != root; current = current.parent)
            parts.Add(current.name);
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static void Select(string classId)
    {
        GameSession.SelectClass(classId);
        WorldDefinition world = WorldCatalog.Default;
        if (world == null)
        {
            Debug.LogError("没有可用的世界地图，无法开始冒险。");
            return;
        }

        RunSession.StartNew(classId, world);
        SceneManager.LoadScene("S_Battle");
    }
}
