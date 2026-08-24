using System.Collections.Generic;

public enum HeroClass { Warrior, Ranger, Mage, Assassin, Priest }

public sealed class HeroClassProfile
{
    public HeroClass Class { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public string PrimaryPool { get; }
    public int PrimarySlots { get; }
    public string SecondaryWeaponPool { get; }
    public int SecondarySlots { get; }
    public string EquipmentPool { get; }

    public HeroClassProfile(HeroClass heroClass, string displayName, string description,
        string primaryPool, int primarySlots, string secondaryWeaponPool = null,
        int secondarySlots = 0, string equipmentPool = null)
    {
        Class = heroClass;
        DisplayName = displayName;
        Description = description;
        PrimaryPool = primaryPool;
        PrimarySlots = primarySlots;
        SecondaryWeaponPool = secondaryWeaponPool;
        SecondarySlots = secondarySlots;
        EquipmentPool = equipmentPool;
    }
}

public static class ClassCatalog
{
    private static readonly Dictionary<HeroClass, HeroClassProfile> Profiles =
        new Dictionary<HeroClass, HeroClassProfile>
        {
            [HeroClass.Warrior] = new HeroClassProfile(HeroClass.Warrior, "战士",
                "斑驳的铁剑＋生锈的盾牌\n回合开始获得锋利；回合结束获得3护甲。",
                CardCatalog.SwordPool, 3, CardCatalog.ShieldPool, 3),
            [HeroClass.Ranger] = new HeroClassProfile(HeroClass.Ranger, "游侠",
                "老旧反曲弓＋祖母绿项链\n弓牌距离＋1；普通伤害有10%概率免疫。",
                CardCatalog.BowPool, 6, equipmentPool: CardCatalog.EmeraldPool),
            [HeroClass.Mage] = new HeroClassProfile(HeroClass.Mage, "法师",
                "新手法杖＋水晶球\n初始3魔力，回合开始恢复1魔力。",
                CardCatalog.StaffPool, 6, equipmentPool: CardCatalog.CrystalBallPool),
            [HeroClass.Assassin] = new HeroClassProfile(HeroClass.Assassin, "刺客",
                "锋利匕首＋夜行斗篷\n回合开始获得速攻，并可免费移动1格。",
                CardCatalog.DaggerPool, 3, equipmentPool: CardCatalog.CloakPool),
            [HeroClass.Priest] = new HeroClassProfile(HeroClass.Priest, "牧师",
                "微光权杖＋圣链十字\n每回合可消耗一牌治疗；每场战斗可复活一次。",
                CardCatalog.ScepterPool, 6, equipmentPool: CardCatalog.CrossPool),
        };

    public static IEnumerable<HeroClassProfile> All => Profiles.Values;
    public static HeroClassProfile Get(HeroClass heroClass) => Profiles[heroClass];
}

public static class GameSession
{
    private const string ClassKey = "LegendsOfFurry.SelectedClass";
    private static bool hasRuntimeSelection;
    private static HeroClass selectedClass = HeroClass.Warrior;

    public static HeroClass SelectedClass
    {
        get
        {
            if (!hasRuntimeSelection && UnityEngine.PlayerPrefs.HasKey(ClassKey))
                selectedClass = (HeroClass)UnityEngine.PlayerPrefs.GetInt(ClassKey, 0);
            return selectedClass;
        }
    }

    public static void SelectClass(HeroClass heroClass)
    {
        selectedClass = heroClass;
        hasRuntimeSelection = true;
        UnityEngine.PlayerPrefs.SetInt(ClassKey, (int)heroClass);
        UnityEngine.PlayerPrefs.Save();
    }
    public static void ClearSelectedClass()
    {
        hasRuntimeSelection = false;
        UnityEngine.PlayerPrefs.DeleteKey(ClassKey);
        UnityEngine.PlayerPrefs.Save();
    }
}
