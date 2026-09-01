using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 单位脚下的世界空间生命条。运行时生成，朝向 -Z 并沿 X 轴旋转 -90 度。
/// </summary>
public class WorldHealthBar : MonoBehaviour
{
    private const float WorldScale = 0.01f;
    private const float BarWidth = 110f;
    private const float HealthHeight = 12f;
    private const float ArmorHeight = 5f;
    private const float Padding = 2f;
    private static readonly Quaternion BarRotation =
        Quaternion.LookRotation(Vector3.back, Vector3.up) * Quaternion.Euler(-90f, 0f, 0f);

    [SerializeField] private Vector3 worldOffset = Vector3.zero;

    private Unit owner;
    private GameObject displayObject;
    private Image healthFill;
    private Image armorFill;
    private GameObject armorRow;
    private static Sprite whiteSprite;

    public static WorldHealthBar Ensure(Unit unit)
    {
        if (unit == null)
        {
            return null;
        }

        WorldHealthBar bar = unit.GetComponent<WorldHealthBar>();
        return bar != null ? bar : unit.gameObject.AddComponent<WorldHealthBar>();
    }

    private void Awake()
    {
        owner = GetComponent<Unit>();
        if (owner == null)
        {
            enabled = false;
            return;
        }

        Build();
        owner.StatsChanged += Refresh;
        Refresh(owner);
    }

    private void OnDestroy()
    {
        if (owner != null)
        {
            owner.StatsChanged -= Refresh;
        }

        if (displayObject != null)
        {
            Destroy(displayObject);
        }
    }

    private void LateUpdate()
    {
        if (owner == null || displayObject == null)
        {
            return;
        }

        Transform displayTransform = displayObject.transform;
        displayTransform.position = owner.transform.position + Vector3.up * BarLift() + worldOffset;
        displayTransform.rotation = BarRotation;
    }

    private float BarLift()
    {
        Renderer renderer = owner.GetComponentInChildren<Renderer>();
        return renderer != null ? renderer.bounds.size.y + 0.08f : 0.45f;
    }

    private void Build()
    {
        displayObject = new GameObject($"{name}_HealthBar");
        RectTransform canvasRect = displayObject.AddComponent<RectTransform>();
        Canvas canvas = displayObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        canvas.sortingOrder = 32;
        CanvasGroup group = displayObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        canvasRect.sizeDelta = new Vector2(BarWidth, HealthHeight + ArmorHeight + 6f);
        displayObject.transform.localScale = Vector3.one * WorldScale;
        displayObject.transform.rotation = BarRotation;

        Image background = CreateImage("Background", displayObject.transform, new Color(0.07f, 0.08f, 0.1f, 0.88f));
        Stretch(background.rectTransform, 0f, 0f, 0f, 0f);

        armorRow = new GameObject("ArmorRow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        armorRow.transform.SetParent(displayObject.transform, false);
        RectTransform armorRect = (RectTransform)armorRow.transform;
        armorRect.anchorMin = new Vector2(0f, 1f);
        armorRect.anchorMax = new Vector2(1f, 1f);
        armorRect.pivot = new Vector2(0.5f, 1f);
        armorRect.anchoredPosition = new Vector2(0f, -1f);
        armorRect.sizeDelta = new Vector2(-Padding * 2f, ArmorHeight);
        Image armorBackground = armorRow.GetComponent<Image>();
        armorBackground.sprite = WhiteSprite();
        armorBackground.color = new Color(0.12f, 0.16f, 0.24f, 0.9f);
        armorBackground.raycastTarget = false;

        armorFill = CreateImage("ArmorFill", armorRow.transform, new Color(0.55f, 0.78f, 1f, 1f));
        SetupFill(armorFill.rectTransform);

        GameObject healthRow = new GameObject("HealthRow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        healthRow.transform.SetParent(displayObject.transform, false);
        RectTransform healthRect = (RectTransform)healthRow.transform;
        healthRect.anchorMin = new Vector2(0f, 0f);
        healthRect.anchorMax = new Vector2(1f, 0f);
        healthRect.pivot = new Vector2(0.5f, 0f);
        healthRect.anchoredPosition = new Vector2(0f, 1f);
        healthRect.sizeDelta = new Vector2(-Padding * 2f, HealthHeight);
        Image healthBackground = healthRow.GetComponent<Image>();
        healthBackground.sprite = WhiteSprite();
        healthBackground.color = new Color(0.16f, 0.12f, 0.12f, 0.95f);
        healthBackground.raycastTarget = false;

        healthFill = CreateImage("HealthFill", healthRow.transform, HealthColor());
        SetupFill(healthFill.rectTransform);
    }

    private void Refresh(Unit unit)
    {
        if (unit == null || healthFill == null)
        {
            return;
        }

        float healthPercent = unit.MaxHealth <= 0
            ? 0f
            : Mathf.Clamp01((float)unit.CurrentHealth / unit.MaxHealth);
        SetBarFill(healthFill, healthPercent);
        healthFill.color = HealthColor();

        bool showArmor = unit.Armor > 0;
        if (armorRow != null)
        {
            armorRow.SetActive(showArmor);
        }

        if (showArmor && armorFill != null)
        {
            SetBarFill(armorFill, Mathf.Clamp01((float)unit.Armor / Mathf.Max(1, unit.MaxHealth)));
        }
    }

    private Color HealthColor()
    {
        if (owner != null && owner.Faction == UnitFaction.Enemy)
        {
            return new Color(0.9f, 0.24f, 0.22f, 1f);
        }

        return new Color(0.3f, 0.84f, 0.4f, 1f);
    }

    private static Image CreateImage(string objectName, Transform parent, Color color)
    {
        GameObject imageObject = new GameObject(
            objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        imageObject.transform.SetParent(parent, false);
        Image image = imageObject.GetComponent<Image>();
        image.sprite = WhiteSprite();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static void SetupFill(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        Image image = rect.GetComponent<Image>();
        image.sprite = WhiteSprite();
        image.type = Image.Type.Simple;
    }

    private static void SetBarFill(Image fill, float amount)
    {
        if (fill == null)
        {
            return;
        }

        float percent = Mathf.Clamp01(amount);
        fill.rectTransform.anchorMin = Vector2.zero;
        fill.rectTransform.anchorMax = new Vector2(percent, 1f);
        fill.rectTransform.offsetMin = Vector2.zero;
        fill.rectTransform.offsetMax = Vector2.zero;
    }

    private static Sprite WhiteSprite()
    {
        if (whiteSprite != null)
        {
            return whiteSprite;
        }

        Texture2D texture = Texture2D.whiteTexture;
        whiteSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            1f);
        return whiteSprite;
    }

    private static void Stretch(RectTransform rect, float left, float right, float bottom, float top)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }
}
