using System;
using System.Linq;
using LegendsOfFurry.Content.Contracts;
using LegendsOfFurry.Content.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class ClassSelectionController : MonoBehaviour
{
    private TMP_FontAsset font;

    private void Awake()
    {
        font = Resources.Load<TMP_FontAsset>("Fonts & Materials/SourceHanSansSC-Regular SDF") ?? TMP_Settings.defaultFontAsset;
        EnsureEventSystem();
        BuildInterface();
    }

    /// <summary>
    /// 从数据库职业列表构建选择界面；内容未加载时阻止玩家进入无数据战斗。
    /// </summary>
    private void BuildInterface()
    {
        GameObject canvasObject = new GameObject("ClassSelectCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        Image background = CreateImage("Background", canvasObject.transform, new Color(0.529f, 0.808f, 0.922f, 1f));
        Stretch(background.rectTransform);

        TMP_Text title = CreateText("Title", canvasObject.transform, 58f, FontStyles.Bold);
        title.rectTransform.anchorMin = new Vector2(0.15f, 0.82f);
        title.rectTransform.anchorMax = new Vector2(0.85f, 0.96f);
        title.rectTransform.offsetMin = title.rectTransform.offsetMax = Vector2.zero;
        title.text = "鸿叶 · 选择职业";
        title.alignment = TextAlignmentOptions.Center;

        TMP_Text subtitle = CreateText("Subtitle", canvasObject.transform, 24f, FontStyles.Normal);
        subtitle.rectTransform.anchorMin = new Vector2(0.2f, 0.75f);
        subtitle.rectTransform.anchorMax = new Vector2(0.8f, 0.82f);
        subtitle.rectTransform.offsetMin = subtitle.rectTransform.offsetMax = Vector2.zero;
        subtitle.text = "选择后进入战斗，对手：太糕";
        subtitle.alignment = TextAlignmentOptions.Center;
        subtitle.color = new Color(0.72f, 0.8f, 0.9f);

        GameObject row = new GameObject("ClassCards", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(canvasObject.transform, false);
        RectTransform rowRect = (RectTransform)row.transform;
        rowRect.anchorMin = new Vector2(0.05f, 0.18f); rowRect.anchorMax = new Vector2(0.95f, 0.72f);
        rowRect.offsetMin = rowRect.offsetMax = Vector2.zero;
        HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 18f; layout.padding = new RectOffset(10, 10, 10, 10);
        layout.childControlWidth = true; layout.childControlHeight = true; layout.childForceExpandWidth = true; layout.childForceExpandHeight = true;

        if (!ContentRuntime.IsLoaded)
        {
            throw new InvalidOperationException($"职业选择无法读取数据库内容包：{ContentRuntime.LoadError}");
        }
        foreach (ClassProfileDefinition profile in ContentRuntime.Registry.ClassProfiles
                     .Where(item => item.Enabled).OrderBy(item => item.SortOrder))
        {
            if (!Enum.TryParse(profile.ClassId, true, out HeroClass heroClass))
            {
                Debug.LogWarning($"跳过未知职业 ID：{profile.ClassId}", this);
                continue;
            }
            CreateClassButton(row.transform, profile, heroClass);
        }
    }

    /// <summary>
    /// 使用数据库职业名称、描述和排序结果创建一个职业选择按钮。
    /// </summary>
    /// <param name="parent">按钮所在的横向布局。</param>
    /// <param name="profile">数据库职业定义。</param>
    /// <param name="heroClass">与场景会话兼容的职业枚举。</param>
    private void CreateClassButton(Transform parent, ClassProfileDefinition profile, HeroClass heroClass)
    {
        GameObject obj = new GameObject(profile.DisplayName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        obj.transform.SetParent(parent, false);
        Image image = obj.GetComponent<Image>();
        image.color = ClassColor(heroClass);
        Button button = obj.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.highlightedColor = Color.Lerp(image.color, Color.white, 0.18f);
        colors.pressedColor = Color.Lerp(image.color, Color.black, 0.2f);
        button.colors = colors;
        button.onClick.AddListener(() => Select(heroClass));

        TMP_Text name = CreateText("Name", obj.transform, 34f, FontStyles.Bold);
        name.rectTransform.anchorMin = new Vector2(0.06f, 0.68f); name.rectTransform.anchorMax = new Vector2(0.94f, 0.94f);
        name.rectTransform.offsetMin = name.rectTransform.offsetMax = Vector2.zero;
        name.text = profile.DisplayName; name.alignment = TextAlignmentOptions.Center;

        TMP_Text description = CreateText("Description", obj.transform, 20f, FontStyles.Normal);
        description.rectTransform.anchorMin = new Vector2(0.08f, 0.10f); description.rectTransform.anchorMax = new Vector2(0.92f, 0.66f);
        description.rectTransform.offsetMin = description.rectTransform.offsetMax = Vector2.zero;
        description.text = profile.Description; description.alignment = TextAlignmentOptions.TopLeft; description.textWrappingMode = TextWrappingModes.Normal;
    }

    private void Select(HeroClass heroClass)
    {
        GameSession.SelectClass(heroClass);
        SceneManager.LoadScene("S_Battle");
    }

    private void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null) return;
        new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
    }

    private TMP_Text CreateText(string name, Transform parent, float size, FontStyles style)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        obj.transform.SetParent(parent, false);
        TMP_Text text = obj.GetComponent<TMP_Text>();
        text.font = font; text.fontSize = size; text.fontStyle = style; text.color = Color.white; text.raycastTarget = false;
        return text;
    }

    private static Image CreateImage(string name, Transform parent, Color color)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        obj.transform.SetParent(parent, false);
        Image image = obj.GetComponent<Image>(); image.color = color; image.raycastTarget = false; return image;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static Color ClassColor(HeroClass heroClass) => heroClass switch
    {
        HeroClass.Warrior => new Color(0.36f, 0.16f, 0.13f, 1f),
        HeroClass.Ranger => new Color(0.13f, 0.34f, 0.22f, 1f),
        HeroClass.Mage => new Color(0.14f, 0.25f, 0.48f, 1f),
        HeroClass.Assassin => new Color(0.27f, 0.16f, 0.38f, 1f),
        HeroClass.Priest => new Color(0.46f, 0.36f, 0.13f, 1f),
        _ => Color.gray
    };
}
