#nullable enable
using System;

namespace LegendsOfFurry.Content.Contracts
{

/// <summary>
/// Exposes the stable identity of a definition without imposing a shared serialized property name.
/// Methods are used deliberately so JSON serializers do not add synthetic identity fields to schema v1.
/// </summary>
public interface IContentDefinition
{
    /// <summary>Returns the stable content kind used by registries and behavior ownership.</summary>
    string GetDefinitionKind();

    /// <summary>Returns the stable, package-authored definition ID.</summary>
    string GetDefinitionId();
}

/// <summary>
/// Connects a mutable runtime instance to the immutable definition that created it.
/// </summary>
/// <typeparam name="TDefinition">The definition type that owns the instance's static data.</typeparam>
public interface IContentInstance<out TDefinition> where TDefinition : IContentDefinition
{
    /// <summary>Gets the unique runtime identity of this instance.</summary>
    string InstanceId { get; }

    /// <summary>Gets the shared definition referenced by this instance.</summary>
    TDefinition Definition { get; }
}

/// <summary>
/// Defines the stable ID grammar shared by the editor, publisher, runtime loader, and registries.
/// </summary>
public static class ContentId
{
    public const string FormatDescription =
        "must start with a lowercase letter and contain only lowercase letters, digits, '.', '_' or '-'";

    /// <summary>
    /// Checks a candidate without allocating or depending on the current culture.
    /// </summary>
    /// <param name="value">The external or authored ID to validate.</param>
    /// <returns>True when the value follows the schema v1 stable ID grammar.</returns>
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
    /// Returns a validated ID or throws at the boundary where invalid external content enters runtime code.
    /// </summary>
    /// <param name="value">The candidate stable ID.</param>
    /// <param name="parameterName">The parameter or field name included in the exception.</param>
    /// <returns>The original ID after validation.</returns>
    /// <exception cref="ArgumentException">Thrown when the candidate does not follow the shared grammar.</exception>
    public static string Require(string? value, string parameterName)
    {
        if (!IsValid(value))
        {
            throw new ArgumentException($"Content ID {FormatDescription}: {value}", parameterName);
        }

        return value!;
    }
}

/// <summary>Stable definition and behavior-owner kinds used across package and runtime layers.</summary>
public static class ContentDefinitionKinds
{
    public const string Card = "card";
    public const string Status = "status";
    public const string Class = "class";
    public const string Deck = "deck";
    public const string CardPool = "pool";
    public const string Rarity = "rarity";
    public const string Asset = "asset";
    public const string Character = "character";
    public const string Equipment = "equipment";

    /// <summary>Returns whether the key names a definition collection understood by package composition.</summary>
    public static bool IsKnown(string kind) => kind == Card || kind == Status || kind == Class ||
        kind == Deck || kind == CardPool || kind == Rarity || kind == Asset ||
        kind == Character || kind == Equipment;

    /// <summary>
    /// Checks whether a definition kind may own a behavior graph in the target architecture.
    /// Character and equipment are reserved now so later schema phases do not invent new ownership semantics.
    /// </summary>
    /// <param name="kind">The authored owner kind.</param>
    /// <returns>True for card, status, class, character, or equipment.</returns>
    public static bool CanOwnBehavior(string? kind)
    {
        return kind == Card || kind == Status || kind == Class ||
               kind == Character || kind == Equipment;
    }
}

/// <summary>Stable external keys for the card zones supported by schema v1 effects.</summary>
public static class ContentCardZoneKeys
{
    public const string Hand = "hand";
    public const string Draw = "draw";
    public const string Discard = "discard";
    public const string Exhaust = "exhaust";
    public const string All = "all";

    /// <summary>Checks whether a key names one concrete mutable card zone.</summary>
    /// <param name="key">The authored zone key.</param>
    /// <returns>True for hand, draw, discard, or exhaust.</returns>
    public static bool IsConcrete(string? key)
    {
        return key == Hand || key == Draw || key == Discard || key == Exhaust;
    }

    /// <summary>Checks whether a key names one concrete zone or the all-zones query.</summary>
    /// <param name="key">The authored zone key.</param>
    /// <returns>True for a concrete zone or all.</returns>
    public static bool IsConcreteOrAll(string? key)
    {
        return IsConcrete(key) || key == All;
    }
}

/// <summary>Stable equipment slots understood by the current battle HUD.</summary>
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
