using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 单张手牌的视图与输入组件。
/// 负责显示数据库卡牌的运行时投影、悬浮放大、点击打出以及相关过渡动画。
/// </summary>
[RequireComponent(typeof(Image))]
public class HandCardView : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    private HandCardSystem owner;
    private RectTransform rectTransform;
    private Image cardImage;
    private Coroutine animationCoroutine;
    private Vector2 layoutPosition;
    private bool isHovered;
    private bool isPlaying;
    private bool isAwaitingTarget;
    private CardData cardData;
    private CardInstance cardInstance;

    public Sprite Sprite => cardImage.sprite;
    public CardData Data => cardData;
    public CardInstance Instance => cardInstance;
    public bool IsAwaitingTarget => isAwaitingTarget;

    /// <summary>
    /// 绑定数据库卡牌实例并刷新卡图、占位卡面和交互状态。
    /// </summary>
    /// <param name="hand">拥有该视图的手牌系统。</param>
    /// <param name="instance">持有权威 CardDefinition 的运行时实例。</param>
    public void Initialize(HandCardSystem hand, CardInstance instance)
    {
        owner = hand;
        cardInstance = instance;
        cardData = instance.Data;

        rectTransform = (RectTransform)transform;
        cardImage = GetComponent<Image>();
        cardImage.sprite = cardData.artwork;
        cardImage.preserveAspect = false;
        cardImage.color = cardData.artwork != null ? Color.white : RarityColor(cardData.rarity);

        if (cardData.artwork == null)
        {
            BuildPlaceholderFace();
        }
        else
        {
            Transform existing = transform.Find("RuntimeCardText");
            if (existing != null) Destroy(existing.gameObject);
        }
    }

    /// <summary>为没有卡图的数据库卡牌创建文字卡面与高对比度外边框。</summary>
    private void BuildPlaceholderFace()
    {
        Transform existing = transform.Find("RuntimeCardText");
        if (existing != null) Destroy(existing.gameObject);

        Outline border = GetComponent<Outline>() ?? gameObject.AddComponent<Outline>();
        border.enabled = true;
        border.effectColor = new Color(0.96f, 0.88f, 0.62f, 1f);
        border.effectDistance = new Vector2(3f, -3f);
        border.useGraphicAlpha = false;

        GameObject root = new GameObject("RuntimeCardText", typeof(RectTransform));
        root.transform.SetParent(transform, false);
        RectTransform rootRect = (RectTransform)root.transform;
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = new Vector2(10f, 10f);
        rootRect.offsetMax = new Vector2(-10f, -10f);

        TMP_Text title = CreateText("Name", root.transform, 24f, FontStyles.Bold);
        SetRect(title.rectTransform, new Vector2(0f, 0.72f), Vector2.one);
        title.text = cardData.cardName;
        title.alignment = TextAlignmentOptions.Center;

        TMP_Text cost = CreateText("Cost", root.transform, 26f, FontStyles.Bold);
        cost.rectTransform.anchorMin = cost.rectTransform.anchorMax = new Vector2(0f, 1f);
        cost.rectTransform.pivot = new Vector2(0f, 1f);
        cost.rectTransform.anchoredPosition = new Vector2(4f, -4f);
        cost.rectTransform.sizeDelta = new Vector2(54f, 42f);
        cost.text = cardData.costText;

        TMP_Text rarity = CreateText("Rarity", root.transform, 16f, FontStyles.Bold);
        rarity.rectTransform.anchorMin = rarity.rectTransform.anchorMax = new Vector2(1f, 1f);
        rarity.rectTransform.pivot = new Vector2(1f, 1f);
        rarity.rectTransform.anchoredPosition = new Vector2(-4f, -6f);
        rarity.rectTransform.sizeDelta = new Vector2(56f, 32f);
        rarity.text = RarityName(cardData.rarity);
        rarity.alignment = TextAlignmentOptions.Right;

        TMP_Text rules = CreateText("Rules", root.transform, 16f, FontStyles.Normal);
        SetRect(rules.rectTransform, Vector2.zero, new Vector2(1f, 0.72f));
        rules.text = cardData.description;
        rules.alignment = TextAlignmentOptions.TopLeft;
        rules.textWrappingMode = TextWrappingModes.Normal;
        rules.overflowMode = TextOverflowModes.Ellipsis;
    }

    private static TMP_Text CreateText(string name, Transform parent, float size, FontStyles style)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        obj.transform.SetParent(parent, false);
        TMP_Text text = obj.GetComponent<TMP_Text>();
        text.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/SourceHanSansSC-Regular SDF") ?? TMP_Settings.defaultFontAsset;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private static void SetRect(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = new Vector2(6f, 6f);
        rect.offsetMax = new Vector2(-6f, -6f);
    }

    private static Color RarityColor(CardRarity rarity) => rarity switch
    {
        CardRarity.Gray => new Color(0.28f, 0.3f, 0.34f, 1f),
        CardRarity.Blue => new Color(0.12f, 0.31f, 0.55f, 1f),
        CardRarity.Purple => new Color(0.38f, 0.17f, 0.52f, 1f),
        CardRarity.Gold => new Color(0.65f, 0.48f, 0.08f, 1f),
        CardRarity.Red => new Color(0.55f, 0.12f, 0.14f, 1f),
        _ => Color.gray
    };

    private static string RarityName(CardRarity rarity) => rarity switch
    {
        CardRarity.Gray => "灰", CardRarity.Blue => "蓝", CardRarity.Purple => "紫",
        CardRarity.Gold => "金", CardRarity.Red => "红", _ => rarity.ToString()
    };

    public void SetLayoutPosition(Vector2 position, bool immediate = false)
    {
        layoutPosition = position;

        if (isHovered || isPlaying || isAwaitingTarget)
        {
            return;
        }

        StartAnimation(position, Vector3.one, immediate ? 0f : 0.18f);
    }

    /// <summary>进入选目标状态：卡牌抬起待命，确认或取消前不回手牌扇形。</summary>
    public void SetAwaitingTarget(bool awaiting)
    {
        isAwaitingTarget = awaiting;
        isHovered = false;

        if (isPlaying)
        {
            return;
        }

        if (awaiting)
        {
            StartAnimation(layoutPosition + Vector2.up * 130f, Vector3.one * 1.18f, 0.15f);
            return;
        }

        StartAnimation(layoutPosition, Vector3.one, 0.15f);
    }

    public void PlayDrawAnimation()
    {
        rectTransform.localScale = Vector3.zero;
        StartAnimation(layoutPosition, Vector3.one, 0.22f);
    }

    public void PlayCard()
    {
        if (isPlaying)
        {
            return;
        }

        isPlaying = true;
        isHovered = false;
        isAwaitingTarget = false;
        StopCurrentAnimation();
        animationCoroutine = StartCoroutine(PlayRoutine());
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (isPlaying)
        {
            return;
        }

        isHovered = true;
        transform.SetAsLastSibling();
        if (!isAwaitingTarget)
        {
            StartAnimation(layoutPosition + Vector2.up * 55f, Vector3.one * 1.22f, 0.12f);
        }

        owner.ShowCardDetails(this);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (isPlaying)
        {
            return;
        }

        isHovered = false;
        if (!isAwaitingTarget)
        {
            StartAnimation(layoutPosition, Vector3.one, 0.12f);
        }

        owner.HideCardDetails(this);
        owner.RestoreCardOrder();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left && !isPlaying)
        {
            owner.PlayCard(this);
        }
    }

    private void StartAnimation(Vector2 targetPosition, Vector3 targetScale, float duration)
    {
        StopCurrentAnimation();
        animationCoroutine = StartCoroutine(
            AnimateRoutine(targetPosition, targetScale, duration));
    }

    private IEnumerator AnimateRoutine(
        Vector2 targetPosition,
        Vector3 targetScale,
        float duration)
    {
        Vector2 startPosition = rectTransform.anchoredPosition;
        Vector3 startScale = rectTransform.localScale;

        if (duration <= 0f)
        {
            rectTransform.anchoredPosition = targetPosition;
            rectTransform.localScale = targetScale;
            animationCoroutine = null;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            t = 1f - Mathf.Pow(1f - t, 3f);
            rectTransform.anchoredPosition = Vector2.Lerp(startPosition, targetPosition, t);
            rectTransform.localScale = Vector3.Lerp(startScale, targetScale, t);
            yield return null;
        }

        rectTransform.anchoredPosition = targetPosition;
        rectTransform.localScale = targetScale;
        animationCoroutine = null;
    }

    private IEnumerator PlayRoutine()
    {
        Vector2 startPosition = rectTransform.anchoredPosition;
        Vector3 startScale = rectTransform.localScale;
        Color startColor = cardImage.color;
        const float duration = 0.28f;
        float elapsed = 0f;

        transform.SetAsLastSibling();

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            rectTransform.anchoredPosition =
                Vector2.Lerp(startPosition, startPosition + Vector2.up * 240f, t);
            rectTransform.localScale =
                Vector3.Lerp(startScale, Vector3.one * 1.35f, t);
            cardImage.color = new Color(
                startColor.r, startColor.g, startColor.b, 1f - t);
            yield return null;
        }

        Destroy(gameObject);
    }

    private void StopCurrentAnimation()
    {
        if (animationCoroutine != null)
        {
            StopCoroutine(animationCoroutine);
            animationCoroutine = null;
        }
    }
}
