using System;
using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// 棋盘单位组件。保存逻辑坐标、阵营和战斗属性，处理选中脉冲与移动动画。
/// 行动点由 BoardClickController 统一管理，Unit 只执行已经批准的移动。
/// </summary>
public class Unit : MonoBehaviour
{
    [Header("棋盘")]
    [SerializeField] private BoardGenerator board;

    [Header("阵营")]
    [SerializeField] private UnitFaction faction = UnitFaction.Player;

    [Header("棋子高度")]
    [SerializeField] private float heightOffset = 0.2f;

    [Header("移动动画")]
    [SerializeField] private float moveDuration = 0.35f;
    [SerializeField] private float jumpHeight = 0.25f;

    [Header("选中效果")]
    [SerializeField] private float selectedPulseAmount = 0.08f;
    [SerializeField] private float selectedPulseSpeed = 5f;

    [Header("战斗属性")]
    [SerializeField, Min(1)] private int maxHealth = 10;
    [SerializeField] private int currentHealth;
    [SerializeField, Min(0)] private int armor;
    [SerializeField, Min(0)] private int attackDamage = 3;
    [SerializeField, Min(1)] private int moveStepsPerTurn = 2;

    [SerializeField] private string displayName;

    [Header("战斗 UI")]
    [SerializeField] private TMP_Text healthText;
    [SerializeField] private TMP_Text armorText;

    private Coroutine moveCoroutine;
    private Coroutine hitRoutine;
    private Vector3 normalScale;
    private bool isSelected;
    private bool isPlaced;
    private float hitPunch;
    private CombatantState combatState;

    public Vector2Int Position { get; private set; }
    public BoardGenerator Board => board;
    public UnitFaction Faction => faction;
    public bool IsMoving => moveCoroutine != null;
    public int MaxHealth => maxHealth;
    public int CurrentHealth => currentHealth;
    public int Armor => armor;
    public int AttackDamage => attackDamage;
    public int MoveStepsPerTurn => moveStepsPerTurn;
    public bool IsAlive => currentHealth > 0;
    public bool IsPlayer => faction == UnitFaction.Player;
    public string DisplayName => string.IsNullOrEmpty(displayName) ? gameObject.name : displayName;
    public CombatantState State => combatState != null ? combatState : combatState = GetComponent<CombatantState>() ?? gameObject.AddComponent<CombatantState>();

    public event Action<Unit> Died;
    public event Action<Unit> StatsChanged;
    public event Action<DamageRequest> BeforeDamage;
    public event Action<DamageResolution> AfterDamage;

    private void Awake()
    {
        normalScale = transform.localScale;
        currentHealth = maxHealth;
        armor = Mathf.Max(0, armor);
        combatState = GetComponent<CombatantState>() ?? gameObject.AddComponent<CombatantState>();
        UpdateCombatUI();
        WorldHealthBar.Ensure(this);
    }

    private void OnDestroy()
    {
        if (board != null && isPlaced)
        {
            board.RemoveOccupant(this);
        }
    }

    private void Update()
    {
        float pulse = 1f;
        if (isSelected)
        {
            pulse += Mathf.Sin(Time.time * selectedPulseSpeed) * selectedPulseAmount;
        }

        transform.localScale = normalScale * pulse * (1f + hitPunch);
    }

    public void SetFaction(UnitFaction value)
    {
        faction = value;
        StatsChanged?.Invoke(this);
    }

    public void SetBoard(BoardGenerator value)
    {
        board = value;
    }

    public void ConfigureCombatant(string shownName, int healthMaximum, int damage, int movement)
    {
        displayName = shownName;
        maxHealth = Mathf.Max(1, healthMaximum);
        currentHealth = maxHealth;
        attackDamage = Mathf.Max(0, damage);
        moveStepsPerTurn = Mathf.Max(1, movement);
        armor = 0;
        NotifyStatsChanged();
    }

    /// <summary>从职业内容配置更新生命上限，并把当前生命恢复到新上限。</summary>
    public void ConfigureMaximumHealth(int healthMaximum)
    {
        maxHealth = Mathf.Max(1, healthMaximum);
        currentHealth = maxHealth;
        NotifyStatsChanged();
    }

    public void Revive(int health)
    {
        currentHealth = Mathf.Clamp(health, 1, maxHealth);
        NotifyStatsChanged();
    }

    public void BindCombatUI(TMP_Text health, TMP_Text armorLabel)
    {
        healthText = health;
        armorText = armorLabel;
        UpdateCombatUI();
    }

    /// <summary>无消耗地放到目标格，主要用于开局放置。</summary>
    public void MoveTo(int x, int z)
    {
        if (board == null || !board.TryGetCell(x, z, out BoardCell targetCell))
        {
            return;
        }

        Vector3 targetPosition = targetCell.transform.position;
        targetPosition.y += heightOffset;
        transform.position = targetPosition;

        Vector2Int previous = Position;
        Position = new Vector2Int(x, z);
        board.SetOccupant(this, Position, isPlaced ? previous : (Vector2Int?)null);
        isPlaced = true;

        Debug.Log($"{gameObject.name} 移动到了 ({x}, {z})");
    }

    /// <summary>尝试启动移动动画；目标无效或正在移动时返回 false。</summary>
    public bool MoveToAnimated(int x, int z)
    {
        Vector2Int targetCoordinate = new Vector2Int(x, z);

        if (IsMoving || targetCoordinate == Position ||
            board == null ||
            !board.TryGetCell(x, z, out BoardCell targetCell) ||
            board.IsOccupied(x, z, this))
        {
            return false;
        }

        Vector3 targetPosition = targetCell.transform.position;
        targetPosition.y += heightOffset;

        Vector2Int previous = Position;
        Position = targetCoordinate;
        board.SetOccupant(this, Position, isPlaced ? previous : (Vector2Int?)null);
        isPlaced = true;

        moveCoroutine = StartCoroutine(MoveRoutine(targetPosition));
        return true;
    }

    public void SetSelected(bool selected)
    {
        isSelected = selected;

        if (!selected)
        {
            transform.localScale = normalScale;
        }
    }

    public void Heal(int amount)
    {
        if (amount <= 0 || !IsAlive)
        {
            return;
        }

        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        NotifyStatsChanged();
    }

    public void AddArmor(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        int reduced = Mathf.Max(0, amount - State.Get(CombatStatus.Broken));
        armor += reduced;
        NotifyStatsChanged();
    }

    /// <summary>承受伤害。护甲会优先吸收伤害，返回实际损失的生命值。</summary>
    public int TakeDamage(int amount)
    {
        return TakeTypedDamage(amount, DamageType.Normal);
    }

    /// <summary>
    /// 按伤害类型结算免疫、护甲、易损、复活和死亡，并可在自动测试中关闭纯表现协程。
    /// </summary>
    /// <param name="amount">进入单位伤害管线的非负数值。</param>
    /// <param name="damageType">决定免疫和是否穿透护甲的伤害类型。</param>
    /// <param name="source">可选伤害来源单位。</param>
    /// <param name="showPresentation">是否播放受击反馈和伤害数字；规则结果不受影响。</param>
    /// <returns>实际损失的生命值。</returns>
    public int TakeTypedDamage(int amount, DamageType damageType, Unit source = null, bool showPresentation = true)
    {
        return ResolveDamage(new DamageRequest(source, this, amount, damageType, showPresentation)).HealthDamage;
    }

    /// <summary>依次执行伤害前事件、免疫/易损、护甲、生命、复活、死亡和伤害后事件。</summary>
    /// <param name="request">允许监听器在实际应用前修改的伤害请求。</param>
    /// <returns>本次结算的结构化结果。</returns>
    public DamageResolution ResolveDamage(DamageRequest request)
    {
        DamageResolution resolution = new DamageResolution { Request = request };
        if (request == null || request.Target != this || request.Amount <= 0 || !IsAlive)
        {
            return resolution;
        }
        BeforeDamage?.Invoke(request);
        if (request.Cancelled || request.Amount <= 0)
        {
            AfterDamage?.Invoke(resolution);
            return resolution;
        }

        int amount = request.Amount;
        DamageType damageType = request.DamageType;

        if (damageType == DamageType.Normal)
        {
            if (State.Has(CombatStatus.NormalImmunity))
            {
                resolution.WasDodgedOrImmune = true;
                AfterDamage?.Invoke(resolution);
                return resolution;
            }
            if (State.Has(CombatStatus.DodgeNextNormal))
            {
                State.Reduce(CombatStatus.DodgeNextNormal);
                resolution.WasDodgedOrImmune = true;
                AfterDamage?.Invoke(resolution);
                return resolution;
            }
            if (State.NormalDamageAvoidChance > 0f && UnityEngine.Random.value < State.NormalDamageAvoidChance)
            {
                resolution.WasDodgedOrImmune = true;
                AfterDamage?.Invoke(resolution);
                return resolution;
            }
        }

        if (State.Has(CombatStatus.Vulnerable))
            amount = Mathf.FloorToInt(amount * 1.5f);

        bool bypassArmor = damageType == DamageType.Dark || damageType == DamageType.Poison || damageType == DamageType.True;
        int absorbed = bypassArmor ? 0 : Mathf.Min(armor, amount);
        resolution.FinalDamage = amount;
        resolution.AbsorbedByArmor = absorbed;
        armor -= absorbed;
        int healthDamage = amount - absorbed;
        int actualHealthDamage = Mathf.Min(currentHealth, healthDamage);
        resolution.HealthDamage = actualHealthDamage;
        currentHealth = Mathf.Max(0, currentHealth - healthDamage);
        NotifyStatsChanged();
        if (request.ShowPresentation)
        {
            PlayHitReaction(healthDamage > 0
                ? new Color(1f, 0.35f, 0.28f)
                : new Color(0.75f, 0.85f, 1f));
            CombatVfx.PlayDamageNumber(
                transform.position,
                amount,
                healthDamage > 0 ? new Color(1f, 0.45f, 0.35f) : new Color(0.7f, 0.85f, 1f));
        }

        if (!IsAlive && State.ReviveAvailable)
        {
            State.ReviveAvailable = false;
            currentHealth = Mathf.Min(5, maxHealth);
            NotifyStatsChanged();
            resolution.Revived = true;
            AfterDamage?.Invoke(resolution);
            return resolution;
        }

        if (!IsAlive)
        {
            resolution.Killed = true;
            Died?.Invoke(this);
        }

        AfterDamage?.Invoke(resolution);
        return resolution;
    }

    public void PlayHitReaction(Color flashColor)
    {
        if (hitRoutine != null)
        {
            StopCoroutine(hitRoutine);
        }

        hitRoutine = StartCoroutine(HitReactionRoutine(flashColor));
    }

    private IEnumerator HitReactionRoutine(Color flashColor)
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        Color[] originalColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            originalColors[i] = renderers[i].sharedMaterial.HasProperty("_BaseColor")
                ? renderers[i].sharedMaterial.GetColor("_BaseColor")
                : renderers[i].sharedMaterial.color;
        }

        const float duration = 0.22f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            hitPunch = (1f - t) * 0.28f;
            Color flash = Color.Lerp(flashColor, Color.white, t);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null)
                {
                    continue;
                }

                renderers[i].GetPropertyBlock(block);
                Color mixed = Color.Lerp(flash, originalColors[i], t);
                block.SetColor("_BaseColor", mixed);
                block.SetColor("_Color", mixed);
                renderers[i].SetPropertyBlock(block);
            }

            yield return null;
        }

        hitPunch = 0f;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null)
            {
                continue;
            }

            renderers[i].SetPropertyBlock(null);
        }

        hitRoutine = null;
    }

    /// <summary>回合开始时清除临时护甲。</summary>
    public void ClearArmor()
    {
        if (armor == 0)
        {
            return;
        }

        armor = 0;
        NotifyStatsChanged();
    }

    private void NotifyStatsChanged()
    {
        UpdateCombatUI();
        StatsChanged?.Invoke(this);
    }

    private void UpdateCombatUI()
    {
        if (healthText != null)
        {
            healthText.text = $"{currentHealth}/{maxHealth}";
        }

        if (armorText != null)
        {
            armorText.text = armor.ToString();
        }
    }

    private void OnValidate()
    {
        maxHealth = Mathf.Max(1, maxHealth);
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        armor = Mathf.Max(0, armor);
        attackDamage = Mathf.Max(0, attackDamage);
        moveStepsPerTurn = Mathf.Max(1, moveStepsPerTurn);
    }

    private IEnumerator MoveRoutine(Vector3 targetPosition)
    {
        Vector3 startPosition = transform.position;
        float duration = Mathf.Max(0.01f, moveDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float easedT = t * t * (3f - 2f * t);
            Vector3 position = Vector3.Lerp(startPosition, targetPosition, easedT);
            position.y += Mathf.Sin(t * Mathf.PI) * jumpHeight;
            transform.position = position;
            yield return null;
        }

        transform.position = targetPosition;
        moveCoroutine = null;
        Debug.Log($"{gameObject.name} 移动到了 {Position}");
    }
}
