/** 策划界面中文名；程序键仅作回退显示。 */

export const effectLabels: Record<string, string> = {
  damage: '造成伤害', heal: '治疗', gain_armor: '获得护甲', revive: '复活', knock_back: '击退',
  add_status: '添加状态', set_status: '设置状态', reduce_status: '减少状态', remove_status: '移除状态',
  clear_statuses: '清除状态', consume_owner_status: '消耗自己的状态', transform_owner_status: '转化自己的状态',
  modify_action_points: '修改行动力', modify_max_action_points: '修改行动力上限',
  modify_mana: '修改魔力', spend_resource: '消耗资源', draw_cards: '抽牌', generate_card: '生成卡牌',
  move_cards: '移动卡牌', remove_cards_by_query: '按条件移除卡牌', modify_card_runtime_value: '修改卡牌数值',
  reveal_top_cards_and_choose_discard: '查看牌顶并弃牌', play_top_cards_for_free: '免费打出牌顶',
  begin_free_move: '开始免费移动', end_turn: '结束回合', no_op: '无操作', play_vfx: '播放特效', play_sfx: '播放音效',
  modify_query_value: '修改问询数值', cancel_query: '取消这次问询',
}

export const triggerLabels: Record<string, string> = {
  on_play: '打出时', on_draw: '抽到时', on_added_to_hand: '加入手牌时',
  on_turn_end_in_hand: '留在手牌且回合结束时', on_unit_turn_start: '单位回合开始时',
  on_unit_turn_end: '单位回合结束时', on_status_changed: '状态层数变化时',
  on_activated_ability: '使用职业主动技时',
  query_outgoing_attack_damage: '问询：打出的攻击伤害', query_incoming_damage: '问询：受到的伤害',
  query_after_attack: '问询：攻击之后', query_attack_hit_count: '问询：攻击段数',
  query_armor_gain: '问询：获得护甲', query_armor_retention: '问询：护甲是否保留',
  query_maximum_action_points: '问询：行动力上限', query_move_steps: '问询：可走步数',
  query_mana_gain: '问询：回魔', query_can_play_card: '问询：能否打出这张牌',
  query_can_take_turn: '问询：能否行动', query_target_range: '问询：技能距离',
  query_lethal_recovery: '问询：濒死回复',
}

export const nodeLabels: Record<string, string> = {
  sequence: '顺序', condition: '如果', repeat: '重复', foreach: '挨个', effect: '效果',
}

export const familyLabels: Record<string, string> = {
  none: '基础', sword: '剑', shield: '盾', bow: '弓', staff: '法杖', scepter: '手杖',
  dagger: '匕首', necklace: '项链', crystal: '水晶', crystal_ball: '水晶球',
  cross: '十字架', cloak: '斗篷', equipment: '装备', test: '测试',
}

export const rarityLabels: Record<string, string> = {
  gray: '灰', blue: '蓝', purple: '紫', gold: '金', red: '红',
}

export const conditionLabels: Record<string, string> = {
  all: '全部成立', any: '任一成立', not: '不成立',
  target_is_alive: '目标活着', target_is_dead: '目标已死', target_is_self: '目标是自己',
  target_team_is: '目标阵营是', target_has_armor: '目标有护甲', target_has_status: '目标有状态',
  card_has_tag: '卡有标签', card_in_pool: '卡在卡池', card_rarity_is: '卡稀有度是',
  target_killed_by_this_card: '这张卡击杀了目标', damage_was_applied: '造成了伤害',
  health_damage_greater_than: '生命伤害大于', resource_compare: '资源比较',
  health_compare: '生命比较', armor_compare: '护甲比较',
  spent_action_compare: '已花行动力比较', spent_mana_compare: '已花魔力比较',
  random_chance: '随机概率',
}

export const targetLabels: Record<string, string> = {
  self: '自己', selected_unit: '点选单位', current_graph_target: '当前这个',
  selected_cell: '点选格子', units_around_selected: '点选周围单位',
  units_in_manhattan_range: '曼哈顿范围内单位', units_in_direction: '该方向上的单位',
  units_in_front_area: '面前一片单位', all_allies: '全部友方', all_enemies: '全部敌方',
  all_units: '全部单位', current_card: '当前这张卡', cards_in_hand: '手牌',
  cards_in_draw_pile: '抽牌库', cards_in_discard_pile: '弃牌堆', cards_in_exhaust_pile: '消耗堆',
}

/** 返回键对应的中文名，未知键原样显示。 */
export function zh(map: Record<string, string>, key: string): string {
  return map[key] || key
}
