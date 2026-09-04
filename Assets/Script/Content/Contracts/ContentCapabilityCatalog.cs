#nullable enable
using System;
using System.Collections.Generic;

namespace LegendsOfFurry.Content.Contracts
{
/// <summary>策划行为图与战斗事件发布方共用的稳定触发器 key。</summary>
public static class ContentTriggerKeys
{
    public const string OnPlay = "on_play";
    public const string OnDraw = "on_draw";
    public const string OnAddedToHand = "on_added_to_hand";
    public const string OnTurnEndInHand = "on_turn_end_in_hand";
    public const string OnUnitTurnStart = "on_unit_turn_start";
    public const string OnUnitTurnEnd = "on_unit_turn_end";
    public const string OnStatusChanged = "on_status_changed";
    public const string OnStatusGained = "on_status_gained";
    public const string OnEnteredTerrain = "on_entered_terrain";
    public const string OnMoveCompleted = "on_move_completed";
    public const string OnActivatedAbility = "on_activated_ability";
}

/// <summary>
/// 记录当前发布版本已端到端实现的内容能力，供维护工具、校验器和 Unity 执行器共享。
/// </summary>
public static class ContentCapabilityCatalog
{
    public static IReadOnlyList<string> RuleQueryTriggerKeys { get; } = Array.AsReadOnly(new[]
    {
        "query_outgoing_attack_damage", "query_attack_hit_count", "query_incoming_damage",
        "query_armor_gain", "query_armor_retention", "query_maximum_action_points",
        "query_move_steps", "query_can_play_card", "query_mana_gain", "query_target_range",
        "query_can_take_turn", "query_after_attack", "query_lethal_recovery"
    });
    /// <summary>获取阶段 2 已能在 Unity 行为执行器中触发的 Trigger key。</summary>
    public static IReadOnlyList<string> Phase2ExecutableTriggerKeys { get; } = Array.AsReadOnly(new[]
    {
        ContentTriggerKeys.OnPlay
    });

    /// <summary>获取当前已由卡牌、状态和职业生命周期运行时实现的 Trigger key。</summary>
    public static IReadOnlyList<string> Phase4ExecutableTriggerKeys { get; } = Array.AsReadOnly(new[]
    {
        ContentTriggerKeys.OnPlay, ContentTriggerKeys.OnUnitTurnStart, ContentTriggerKeys.OnUnitTurnEnd
    });

    /// <summary>获取阶段 5 已接入卡牌完整手牌生命周期的 Trigger key。</summary>
    public static IReadOnlyList<string> Phase5ExecutableTriggerKeys { get; } = Array.AsReadOnly(new[]
    {
        ContentTriggerKeys.OnPlay, ContentTriggerKeys.OnDraw, ContentTriggerKeys.OnAddedToHand,
        ContentTriggerKeys.OnTurnEndInHand, ContentTriggerKeys.OnUnitTurnStart, ContentTriggerKeys.OnUnitTurnEnd,
        ContentTriggerKeys.OnStatusGained, ContentTriggerKeys.OnEnteredTerrain, ContentTriggerKeys.OnMoveCompleted
    });

    /// <summary>获取阶段 2 已能由 WPF 创建并在 Unity 同步执行的效果 key。</summary>
    public static IReadOnlyList<string> Phase2ExecutableEffectKeys { get; } = Array.AsReadOnly(new[]
    {
        "damage", "heal", "gain_armor", "draw_cards", "begin_free_move", "end_turn", "no_op"
    });

    /// <summary>获取完整规则引擎当前已注册的首期 Effect key。</summary>
    public static IReadOnlyList<string> Phase3ExecutableEffectKeys { get; } = Array.AsReadOnly(new[]
    {
        "damage", "heal", "gain_armor", "revive", "knock_back",
        "add_status", "set_status", "reduce_status", "remove_status", "clear_statuses",
        "modify_action_points", "modify_max_action_points", "modify_mana", "spend_resource",
        "draw_cards", "generate_card", "move_cards", "remove_cards_by_query",
        "modify_card_runtime_value", "reveal_top_cards_and_choose_discard",
        "play_top_cards_for_free", "begin_free_move", "end_turn", "no_op", "play_vfx", "play_sfx",
        "modify_query_value", "cancel_query", "consume_owner_status", "transform_owner_status",
        "appraise_equipment", "copy_random_positive_status"
    });

    /// <summary>获取阶段 2 基础效果能够直接解析的目标选择器 key。</summary>
    public static IReadOnlyList<string> Phase2ExecutableTargetKeys { get; } = Array.AsReadOnly(new[]
    {
        "self", "selected_unit"
    });

    /// <summary>获取行为图效果节点可直接引用的目标 key，foreach 当前目标不会显示在简易 WPF 表格中。</summary>
    public static IReadOnlyList<string> Phase3ExecutableEffectTargetKeys { get; } = Array.AsReadOnly(new[]
    {
        "self", "selected_unit", "current_graph_target"
    });

    /// <summary>获取当前运行时已经实现的 Condition key；单位标签要等通用标签容器完成后再加入。</summary>
    public static IReadOnlyList<string> Phase3ExecutableConditionKeys { get; } = Array.AsReadOnly(new[]
    {
        "all", "any", "not", "target_is_alive", "target_is_dead", "target_is_self",
        "target_team_is", "target_has_armor", "target_has_status", "card_has_tag",
        "card_in_pool", "card_rarity_is", "target_killed_by_this_card", "damage_was_applied",
        "health_damage_greater_than", "resource_compare", "health_compare", "armor_compare",
        "spent_action_compare", "spent_mana_compare", "random_chance"
    });

    /// <summary>获取当前 foreach 注册表已经实现的目标集合选择器 key。</summary>
    public static IReadOnlyList<string> Phase3ExecutableForEachTargetKeys { get; } = Array.AsReadOnly(new[]
    {
        "self", "selected_unit", "selected_cell", "units_around_selected",
        "units_in_manhattan_range", "units_in_direction", "units_in_front_area",
        "all_allies", "all_enemies", "all_units", "current_card", "cards_in_hand",
        "cards_in_draw_pile", "cards_in_discard_pile", "cards_in_exhaust_pile"
    });
}
}
