using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class CardCatalog
{
    public const string SwordPool = "斑驳的铁剑";
    public const string ShieldPool = "生锈的盾牌";
    public const string BowPool = "老旧反曲弓";
    public const string StaffPool = "新手法杖";
    public const string ScepterPool = "微光权杖";
    public const string DaggerPool = "锋利匕首";
    public const string EmeraldPool = "祖母绿项链";
    public const string CrystalBallPool = "水晶球";
    public const string CrossPool = "圣链十字";
    public const string CloakPool = "夜行斗篷";

    private static readonly List<CardData> Cards = new List<CardData>();
    private static readonly Dictionary<string, CardData> ById = new Dictionary<string, CardData>();
    private static bool initialized;

    public static IReadOnlyList<CardData> All { get { Ensure(); return Cards; } }

    public static CardData Get(string id)
    {
        Ensure();
        return ById.TryGetValue(id, out CardData card) ? card : null;
    }

    public static List<CardData> GetPool(string pool)
    {
        Ensure();
        return Cards.Where(card => card.sourcePool == pool).ToList();
    }

    public static CardData CreateIdentify(string equipmentPool)
    {
        return CardData.Runtime("identify_" + equipmentPool, "鉴定", equipmentPool,
            CardFamily.Equipment, CardRarity.Gray, "0", 0, CardTargetMode.Self,
            false, $"消耗。鉴定【{equipmentPool}】，获得一张临时装备牌，并独立进行20%诅咒判定。",
            true, true);
    }

    private static void Ensure()
    {
        if (initialized) return;
        initialized = true;

        // 斑驳的铁剑（7）
        Add("sword_slash", "劈砍", SwordPool, CardFamily.Sword, CardRarity.Gray, "2", 1, CardTargetMode.Direction, true, "对面前1×3范围内的所有角色造成5点普通伤害（会友伤）。");
        Add("sword_thrust", "刺击", SwordPool, CardFamily.Sword, CardRarity.Gray, "1", 1, CardTargetMode.Unit, true, "造成4点普通伤害；若目标有护甲，额外造成2点普通伤害。");
        Add("sword_flurry", "连斩", SwordPool, CardFamily.Sword, CardRarity.Gray, "x", 1, CardTargetMode.Unit, true, "消耗全部当前行动力，造成X次2点普通伤害。");
        Add("sword_pommel", "柄击", SwordPool, CardFamily.Sword, CardRarity.Gray, "0", 1, CardTargetMode.Unit, true, "造成3点普通伤害，并击退1格。");
        Add("sword_charge", "蓄力", SwordPool, CardFamily.Sword, CardRarity.Blue, "1", 0, CardTargetMode.Self, false, "结束当前回合；下一张剑类伤害牌伤害＋4。");
        Add("sword_guard", "剑刃格挡", SwordPool, CardFamily.Sword, CardRarity.Blue, "1", 1, CardTargetMode.Unit, true, "造成4点普通伤害，并获得3点护甲。");
        Add("sword_desperate_throw", "搏命飞剑", SwordPool, CardFamily.Sword, CardRarity.Purple, "2", 2, CardTargetMode.Unit, true, "造成10点普通伤害，然后移除本场战斗中的所有剑类牌。", true);

        // 生锈的盾牌（5）
        Add("shield_protect", "防护", ShieldPool, CardFamily.Shield, CardRarity.Gray, "1", 0, CardTargetMode.Self, false, "获得5点护甲。");
        Add("shield_polish", "擦拭", ShieldPool, CardFamily.Shield, CardRarity.Gray, "1", 0, CardTargetMode.Self, false, "获得【格挡】：护甲额外保留1回合。");
        Add("shield_bash", "盾击", ShieldPool, CardFamily.Shield, CardRarity.Gray, "1", 1, CardTargetMode.Unit, true, "造成2点普通伤害，并获得5点护甲。");
        Add("shield_raise", "举盾", ShieldPool, CardFamily.Shield, CardRarity.Blue, "0", 0, CardTargetMode.Self, false, "回合结束时获得当前护甲一半（向下取整）；下回合无法打出攻击牌。");
        Add("shield_throw", "飞盾", ShieldPool, CardFamily.Shield, CardRarity.Purple, "2", 2, CardTargetMode.Unit, false, "令目标本轮免疫普通伤害，然后移除本场战斗中的所有盾类牌。", true);

        // 老旧反曲弓（7）
        Add("bow_shot", "射击", BowPool, CardFamily.Bow, CardRarity.Gray, "1", 3, CardTargetMode.Unit, true, "造成4点普通伤害。");
        Add("bow_elbow", "弓壁肘击", BowPool, CardFamily.Bow, CardRarity.Gray, "0", 1, CardTargetMode.Unit, true, "造成3点普通伤害，摸1张牌。");
        Add("bow_roll", "翻滚射击", BowPool, CardFamily.Bow, CardRarity.Gray, "1", 1, CardTargetMode.Unit, true, "造成4点普通伤害，然后可以免费移动1格。");
        Add("bow_charge_snipe", "蓄力狙杀", BowPool, CardFamily.Bow, CardRarity.Blue, "2", 3, CardTargetMode.Unit, true, "造成6点普通伤害；若对自己使用，该牌以后伤害永久＋4，可无限累积。");
        Add("bow_scatter", "散射", BowPool, CardFamily.Bow, CardRarity.Blue, "2", 2, CardTargetMode.Unit, true, "对至多3名角色各造成4点普通伤害。");
        Add("bow_rain", "漫天飞射", BowPool, CardFamily.Bow, CardRarity.Blue, "2", 2, CardTargetMode.AreaCell, true, "对目标3×3范围内所有角色造成3点真实伤害。");
        Add("bow_piercing", "穿云箭", BowPool, CardFamily.Bow, CardRarity.Purple, "3", 1, CardTargetMode.Direction, true, "对所选方向上的所有角色造成10点普通伤害，遇地形障碍停止。");

        // 新手法杖（7）
        Add("staff_fireball", "火球术", StaffPool, CardFamily.Staff, CardRarity.Gray, "1+2", 3, CardTargetMode.Unit, true, "造成6点火属性伤害。");
        Add("staff_ice", "冰锥术", StaffPool, CardFamily.Staff, CardRarity.Gray, "1+2", 3, CardTargetMode.Unit, true, "造成3点冰属性伤害，并施加1层【寒冷】。");
        Add("staff_lightning", "招雷术", StaffPool, CardFamily.Staff, CardRarity.Gray, "1+2", 2, CardTargetMode.Unit, true, "造成4点雷属性伤害；目标1格内其他角色受到2点雷属性伤害。");
        Add("staff_swing", "挥击", StaffPool, CardFamily.Staff, CardRarity.Gray, "1", 1, CardTargetMode.Unit, true, "造成4点普通伤害。");
        Add("staff_meditate", "冥想", StaffPool, CardFamily.Staff, CardRarity.Blue, "1", 0, CardTargetMode.Self, false, "恢复2点魔力；本回合无法使用攻击牌。");
        Add("staff_arcane", "奥术飞弹", StaffPool, CardFamily.Staff, CardRarity.Blue, "1+3", 3, CardTargetMode.Unit, true, "造成5点真实伤害。");
        Add("staff_storm", "魔力风暴", StaffPool, CardFamily.Staff, CardRarity.Purple, "3+x", 2, CardTargetMode.Unit, true, "消耗全部魔力，造成3×X点普通伤害；获得3回合【魔力枯竭】。");

        // 微光权杖（7）
        Add("scepter_elbow", "肘击", ScepterPool, CardFamily.Scepter, CardRarity.Gray, "1", 1, CardTargetMode.Unit, true, "造成4点普通伤害。");
        Add("scepter_tap", "敲击", ScepterPool, CardFamily.Scepter, CardRarity.Gray, "1", 1, CardTargetMode.Unit, true, "造成2点普通伤害，并施加1层【恍惚】1回合。");
        Add("scepter_shine", "照耀", ScepterPool, CardFamily.Scepter, CardRarity.Gray, "1", 3, CardTargetMode.Unit, true, "造成3点光属性伤害，并移除目标所在地块的所有负面状态。");
        Add("scepter_heal", "治愈", ScepterPool, CardFamily.Scepter, CardRarity.Blue, "2", 2, CardTargetMode.Unit, false, "回复5点生命值。");
        Add("scepter_dispel", "驱散", ScepterPool, CardFamily.Scepter, CardRarity.Blue, "2", 2, CardTargetMode.Unit, false, "移除目标所有状态。");
        Add("scepter_light_shield", "光盾", ScepterPool, CardFamily.Scepter, CardRarity.Blue, "2", 2, CardTargetMode.Unit, false, "获得10点护甲。");
        Add("scepter_inner_fire", "心灵之火", ScepterPool, CardFamily.Scepter, CardRarity.Purple, "2", 2, CardTargetMode.Unit, false, "获得2回合【心火】：回合开始获得5护甲，造成伤害＋10%（向下取整）。");

        // 锋利匕首（7）
        Add("dagger_combo", "连击", DaggerPool, CardFamily.Dagger, CardRarity.Gray, "1", 1, CardTargetMode.Unit, true, "造成两次2点普通伤害。");
        Add("dagger_thrust", "突刺", DaggerPool, CardFamily.Dagger, CardRarity.Gray, "1", 1, CardTargetMode.Unit, true, "造成3点真实伤害。");
        Add("dagger_cut", "割伤", DaggerPool, CardFamily.Dagger, CardRarity.Gray, "1", 1, CardTargetMode.Unit, true, "造成1点普通伤害，并施加1层【破损】。");
        Add("dagger_assassinate", "刺杀", DaggerPool, CardFamily.Dagger, CardRarity.Blue, "2", 1, CardTargetMode.Unit, true, "造成4点普通伤害，然后可以免费移动2格。");
        Add("dagger_throat", "封喉", DaggerPool, CardFamily.Dagger, CardRarity.Blue, "1", 1, CardTargetMode.Unit, true, "造成5点真实伤害；成功击杀目标时获得2行动力。");
        Add("dagger_throw", "飞刃", DaggerPool, CardFamily.Dagger, CardRarity.Blue, "1", 2, CardTargetMode.Unit, true, "造成4点普通伤害，并施加3层【中毒】。", true);
        Add("dagger_wrist", "断腕", DaggerPool, CardFamily.Dagger, CardRarity.Purple, "3", 1, CardTargetMode.Unit, true, "造成4点真实伤害，并施加1层【力竭】3回合。");

        // 祖母绿项链（5）
        AddEquipment("emerald_glimmer", "微光", EmeraldPool, CardRarity.Gray, "0", "摸1张牌。", false);
        AddEquipment("emerald_flash", "闪烁", EmeraldPool, CardRarity.Blue, "0", "摸1张牌，获得1行动力。", false);
        AddEquipment("emerald_notice", "瞩目", EmeraldPool, CardRarity.Purple, "0", "闪避下一次普通伤害，获得1行动力。", false);
        AddEquipment("emerald_brilliant", "璀璨", EmeraldPool, CardRarity.Gold, "0", "本轮普通伤害无效，获得2行动力。", false);
        AddEquipment("emerald_dim", "暗淡", EmeraldPool, CardRarity.Red, "/", "无效果，无法打出。", true, true);

        // 水晶球（6）
        Add("crystal_preview", "预演", CrystalBallPool, CardFamily.Equipment, CardRarity.Gray, "1", 2, CardTargetMode.Unit, false, "展示目标牌堆顶3张牌，可将任意张置入弃牌堆。", true, true);
        Add("crystal_divination", "占卜", CrystalBallPool, CardFamily.Equipment, CardRarity.Blue, "1", 2, CardTargetMode.Unit, false, "展示目标牌堆顶一张牌，并免费替其使用；无法使用则弃置。", true, true);
        AddEquipment("crystal_inference", "推论", CrystalBallPool, CardRarity.Blue, "2", "依次免费使用牌堆顶两张牌；无法使用则弃置，诅咒进入手牌。", false);
        AddEquipment("crystal_channel", "通灵", CrystalBallPool, CardRarity.Purple, "2", "属性牌库暂时跳过，本牌当前仅保留占位。", false);
        AddEquipment("crystal_shatter", "碎裂", CrystalBallPool, CardRarity.Red, "1", "回合结束时若在手牌中，受到2点普通伤害。", true, false);
        AddEquipment("crystal_backlash", "反噬", CrystalBallPool, CardRarity.Red, "/", "抽到时受到5点暗属性伤害。", true, true);

        // 圣链十字（5）
        Add("cross_glimmer", "微光", CrossPool, CardFamily.Equipment, CardRarity.Gray, "1", 2, CardTargetMode.Unit, false, "获得3层【再生】。", true, true);
        Add("cross_flash", "闪烁", CrossPool, CardFamily.Equipment, CardRarity.Blue, "1", 2, CardTargetMode.Unit, false, "移除目标所有负面状态，并获得3层【再生】。", true, true);
        Add("cross_brilliant", "璀璨", CrossPool, CardFamily.Equipment, CardRarity.Purple, "2", 2, CardTargetMode.Unit, false, "令已死亡角色以5点生命复活。", true, true);
        AddEquipment("cross_possessed", "入魔", CrossPool, CardRarity.Red, "/", "回合结束时获得3层【腐化】。", true, true);
        AddEquipment("cross_dim", "暗淡", CrossPool, CardRarity.Red, "/", "无效果，无法打出。", true, true);

        // 夜行斗篷（5）
        AddEquipment("cloak_worn", "磨损", CloakPool, CardRarity.Gray, "0", "可以免费移动1格。", false);
        AddEquipment("cloak_clear", "无碍", CloakPool, CardRarity.Gray, "0", "摸1张牌，然后可以免费移动1格。", false);
        AddEquipment("cloak_cover", "遮蔽", CloakPool, CardRarity.Blue, "0", "摸1张牌，获得1行动力。", false);
        AddEquipment("cloak_hide", "隐遁", CloakPool, CardRarity.Purple, "0", "本场战斗行动力上限＋1。", false);
        AddEquipment("cloak_broken", "残缺", CloakPool, CardRarity.Red, "/", "回合结束时获得1层【易损】，持续2回合。", true, true);

        if (Cards.Count != 61)
            Debug.LogError($"卡牌目录数量错误：期望61，实际{Cards.Count}。");
    }

    private static void AddEquipment(string id, string name, string pool, CardRarity rarity,
        string cost, string description, bool curse, bool unplayable = false)
    {
        Add(id, name, pool, CardFamily.Equipment, rarity, cost, 0, CardTargetMode.Self,
            false, description, true, true, curse, unplayable);
    }

    private static void Add(string id, string name, string pool, CardFamily family,
        CardRarity rarity, string cost, int range, CardTargetMode mode, bool attack,
        string description, bool exhaust = false, bool temporary = false,
        bool curse = false, bool unplayable = false)
    {
        CardData card = CardData.Runtime(id, name, pool, family, rarity, cost, range,
            mode, attack, description, exhaust, temporary, curse, unplayable);
        Cards.Add(card);
        ById.Add(id, card);
    }
}

public static class StartingDeckBuilder
{
    public static List<CardInstance> Build(HeroClassProfile profile)
    {
        List<CardInstance> deck = new List<CardInstance>();
        AddWeaponCards(deck, profile.PrimaryPool, profile.PrimarySlots);
        AddWeaponCards(deck, profile.SecondaryWeaponPool, profile.SecondarySlots);
        if (!string.IsNullOrEmpty(profile.EquipmentPool))
            deck.Add(new CardInstance(CardCatalog.CreateIdentify(profile.EquipmentPool)));
        return deck;
    }

    private static void AddWeaponCards(List<CardInstance> deck, string pool, int slots)
    {
        if (string.IsNullOrEmpty(pool) || slots <= 0) return;
        List<CardData> cards = CardCatalog.GetPool(pool)
            .Where(card => card.rarity != CardRarity.Red).ToList();
        List<CardRarity> available = cards.Select(card => card.rarity).Distinct().ToList();
        for (int i = 0; i < slots; i++)
        {
            CardRarity rarity = RollNormalRarity(available);
            List<CardData> sameRarity = cards.Where(card => card.rarity == rarity).ToList();
            deck.Add(new CardInstance(sameRarity[Random.Range(0, sameRarity.Count)]));
        }
    }

    public static CardRarity RollNormalRarity(IReadOnlyCollection<CardRarity> available)
    {
        float total = 0f;
        foreach (CardRarity rarity in available) total += Weight(rarity);
        float roll = Random.value * total;
        foreach (CardRarity rarity in new[] { CardRarity.Gray, CardRarity.Blue, CardRarity.Purple, CardRarity.Gold })
        {
            if (!available.Contains(rarity)) continue;
            roll -= Weight(rarity);
            if (roll <= 0f) return rarity;
        }
        return available.First();
    }

    private static float Weight(CardRarity rarity)
    {
        return rarity switch
        {
            CardRarity.Gray => 0.55f,
            CardRarity.Blue => 0.25f,
            CardRarity.Purple => 0.15f,
            CardRarity.Gold => 0.05f,
            _ => 0f
        };
    }
}
