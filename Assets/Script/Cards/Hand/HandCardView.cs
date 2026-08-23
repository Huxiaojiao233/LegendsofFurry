using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 单张手牌的视图与输入组件。
/// 负责显示 CardData、悬浮放大、点击打出以及相关过渡动画。
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

    public Sprite Sprite => cardImage.sprite;
    public CardData Data => cardData;
    public bool IsAwaitingTarget => isAwaitingTarget;

    public void Initialize(HandCardSystem hand, CardData data)
    {
        owner = hand;
        cardData = data;

        rectTransform = (RectTransform)transform;
        cardImage = GetComponent<Image>();
        cardImage.sprite = data.artwork;
        cardImage.preserveAspect = true;
    }

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
