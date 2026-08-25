#nullable enable
using System;
using System.Collections.Generic;

namespace LegendsOfFurry.Content.Contracts
{
/// <summary>
/// 记录当前发布版本已端到端实现的内容能力，供维护工具、校验器和 Unity 执行器共享。
/// </summary>
public static class ContentCapabilityCatalog
{
    /// <summary>获取阶段 2 已能在 Unity 行为执行器中触发的 Trigger key。</summary>
    public static IReadOnlyList<string> Phase2ExecutableTriggerKeys { get; } = Array.AsReadOnly(new[]
    {
        "on_play"
    });

    /// <summary>获取当前已由卡牌、状态和职业生命周期运行时实现的 Trigger key。</summary>
    public static IReadOnlyList<string> Phase4ExecutableTriggerKeys { get; } = Array.AsReadOnly(new[]
    {
        "on_play", "on_unit_turn_start", "on_unit_turn_end"
    });

    /// <summary>获取阶段 5 已接入卡牌完整手牌生命周期的 Trigger key。</summary>
    public static IReadOnlyList<string> Phase5ExecutableTriggerKeys { get; } = Array.AsReadOnly(new[]
    {
        "on_play", "on_draw", "on_added_to_hand", "on_turn_end_in_hand",
        "on_unit_turn_start", "on_unit_turn_end"
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
        "play_top_cards_for_free", "begin_free_move", "end_turn", "no_op", "play_vfx", "play_sfx"
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
