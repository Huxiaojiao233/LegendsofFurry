#nullable enable
using System;

namespace LegendsOfFurry.Content.Contracts
{

/// <summary>
/// 暴露定义的稳定身份，但不强制共用同一个序列化属性名。
/// 故意用方法而不是字段，避免 JSON 序列化器给 schema v1 额外写出身份字段。
/// </summary>
public interface IContentDefinition
{
    /// <summary>返回注册表和行为归属使用的稳定内容类型。</summary>
    string GetDefinitionKind();

    /// <summary>返回内容包编写的稳定定义 ID。</summary>
    string GetDefinitionId();
}

/// <summary>
/// 把可变运行时实例连接到创建它的不可变定义。
/// </summary>
/// <typeparam name="TDefinition">拥有该实例静态数据的定义类型。</typeparam>
public interface IContentInstance<out TDefinition> where TDefinition : IContentDefinition
{
    /// <summary>获取本次运行时实例的唯一身份。</summary>
    string InstanceId { get; }

    /// <summary>获取该实例引用的共享定义。</summary>
    TDefinition Definition { get; }
}

/// <summary>
/// 定义编辑器、发布器、运行时加载器和注册表共用的稳定 ID 语法。
/// </summary>
public static class ContentId
{
    public const string FormatDescription =
        "must start with a lowercase letter and contain only lowercase letters, digits, '.', '_' or '-'";

    /// <summary>
    /// 检查候选 ID，不分配额外字符串，也不依赖当前区域设置。
    /// </summary>
    /// <param name="value">需要校验的外部或策划编写 ID。</param>
    /// <returns>符合 schema v1 稳定 ID 语法时返回 true。</returns>
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value) || value[0] < 'a' || value[0] > 'z')
        {
            return false;
        }

        for (int index = 1; index < value.Length; index++)
        {
            char character = value[index];
            bool allowed = character >= 'a' && character <= 'z' ||
                           character >= '0' && character <= '9' ||
                           character == '.' || character == '_' || character == '-';
            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 返回通过校验的 ID；无效外部内容进入运行时边界时立即抛出。
    /// </summary>
    /// <param name="value">候选稳定 ID。</param>
    /// <param name="parameterName">写入异常信息的参数或字段名。</param>
    /// <returns>校验后的原始 ID。</returns>
    /// <exception cref="ArgumentException">候选值不符合共用语法时抛出。</exception>
    public static string Require(string? value, string parameterName)
    {
        if (!IsValid(value))
        {
            throw new ArgumentException($"Content ID {FormatDescription}: {value}", parameterName);
        }

        return value!;
    }
}

/// <summary>内容包与运行时共用的定义类型和行为归属类型。</summary>
public static class ContentDefinitionKinds
{
    public const string Card = "card";
    public const string Status = "status";
    public const string Class = "class";
    public const string Deck = "deck";
    public const string CardPool = "pool";
    public const string Rarity = "rarity";
    public const string Asset = "asset";
    public const string Unit = "unit";
    public const string AiProfile = "ai_profile";
    public const string Equipment = "equipment";

    /// <summary>判断该 key 是否对应内容包合成所识别的定义集合。</summary>
    public static bool IsKnown(string kind) => kind == Card || kind == Status || kind == Class ||
        kind == Deck || kind == CardPool || kind == Rarity || kind == Asset ||
        kind == Unit || kind == AiProfile || kind == Equipment;

    /// <summary>
    /// 判断该定义类型是否可以拥有行为图。
    /// 单位和装备现在就预留，避免后续 schema 阶段另造一套归属语义。
    /// </summary>
    /// <param name="kind">策划编写的归属类型。</param>
    /// <returns>卡牌、状态、职业、单位或装备时返回 true。</returns>
    public static bool CanOwnBehavior(string? kind)
    {
        return kind == Card || kind == Status || kind == Class ||
               kind == Unit || kind == Equipment;
    }
}

/// <summary>schema v1 效果支持的牌区稳定外部 key。</summary>
public static class ContentCardZoneKeys
{
    public const string Hand = "hand";
    public const string Draw = "draw";
    public const string Discard = "discard";
    public const string Exhaust = "exhaust";
    public const string All = "all";

    /// <summary>判断该 key 是否对应一个具体可写牌区。</summary>
    /// <param name="key">策划编写的牌区 key。</param>
    /// <returns>hand、draw、discard 或 exhaust 时返回 true。</returns>
    public static bool IsConcrete(string? key)
    {
        return key == Hand || key == Draw || key == Discard || key == Exhaust;
    }

    /// <summary>判断该 key 是具体牌区还是全部牌区查询。</summary>
    /// <param name="key">策划编写的牌区 key。</param>
    /// <returns>具体牌区或 all 时返回 true。</returns>
    public static bool IsConcreteOrAll(string? key)
    {
        return IsConcrete(key) || key == All;
    }
}

/// <summary>当前战斗 HUD 识别的稳定装备槽位。</summary>
public static class ContentEquipmentSlotKeys
{
    public const string Weapon = "weapon";
    public const string Offhand = "offhand";
    public const string Accessory = "accessory";
    public const string Armor = "armor";
    public const string Treasure = "treasure";
    public const string Boot = "boot";

    public static bool IsValid(string value) => value == Weapon || value == Offhand || value == Accessory ||
        value == Armor || value == Treasure || value == Boot;

    /// <summary>主副手开局直接抽从属牌；其余槽位每场只获得一张鉴定牌。</summary>
    public static bool UsesAppraisal(string slotKey) =>
        slotKey == Accessory || slotKey == Armor || slotKey == Treasure || slotKey == Boot;
}
}
