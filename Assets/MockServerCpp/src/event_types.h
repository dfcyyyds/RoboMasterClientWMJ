#ifndef MOCKSERVERCPP_EVENT_TYPES_H_
#define MOCKSERVERCPP_EVENT_TYPES_H_

#include <cstdint>
#include <string>

// ═══════════════════════════════════════════════════════════════
// RoboMaster 2026 事件 ID 定义
// 严格对齐官方通信协议 V1.2.0 §2.2.7 Event
//
// 官方 event_id (1-18) ：含义与参数格式与裁判系统完全一致
// 扩展 event_id (101+)：仿真系统专用，官方协议不涉及
// ═══════════════════════════════════════════════════════════════

namespace EventId {

// ─────────────────────────────────────────────────────────────
// 官方协议 event_id (1-18)
// 参数格式严格遵循协议定义
// ─────────────────────────────────────────────────────────────

/// 1 - 击杀事件  param: "killer_id:victim_id"
constexpr int32_t KILL = 1;

/// 2 - 基地/前哨站被摧毁  param: 被击毁目标 id（如蓝方前哨站=111）
constexpr int32_t STRUCTURE_DESTROYED = 2;

/// 3 - 能量机关可激活次数变化  param: 变化后可激活次数
constexpr int32_t RUNE_CHANCE_CHANGE = 3;

/// 4 - 能量机关当前可进入正在激活状态  无参数
constexpr int32_t RUNE_CAN_ACTIVATE = 4;

/// 5 - 当前能量机关被成功激活的灯臂数量、平均环数  param: "arms:rings"
constexpr int32_t RUNE_ARM_RESULT = 5;

/// 6 - 能量机关被激活（含激活类型）  param: "small"/"large"
constexpr int32_t RUNE_ACTIVATED = 6;

/// 7 - 己方英雄进入部署模式  无参数
constexpr int32_t HERO_DEPLOY_MODE = 7;

/// 8 - 己方英雄造成狙击伤害  param: 累计伤害数量
constexpr int32_t HERO_SNIPER_OWN = 8;

/// 9 - 对方英雄造成狙击伤害  param: 累计伤害数量
constexpr int32_t HERO_SNIPER_ENEMY = 9;

/// 10 - 己方呼叫空中支援  无参数
constexpr int32_t AIR_SUPPORT_OWN = 10;

/// 11 - 己方空中支援被打断  param: 对方剩余可打断次数
constexpr int32_t AIR_SUPPORT_OWN_BROKEN = 11;

/// 12 - 对方呼叫空中支援  无参数
constexpr int32_t AIR_SUPPORT_ENEMY = 12;

/// 13 - 对方空中支援被打断  param: 己方剩余可打断次数
constexpr int32_t AIR_SUPPORT_ENEMY_BROKEN = 13;

/// 14 - 飞镖命中  param:
/// 1=前哨站,2=基地固定,3=基地随机固定,4=基地随机移动,5=基地末端移动
constexpr int32_t DART_HIT = 14;

/// 15 - 飞镖闸门开启  param: "1"=己方,"2"=对方
constexpr int32_t DART_GATE = 15;

/// 16 - 己方基地遭到攻击  无参数（5s 内置冷却）
constexpr int32_t BASE_ATTACKED = 16;

/// 17 - 前哨站停转  param: "1"=己方,"2"=对方
constexpr int32_t OUTPOST_STOP_ROTATE = 17;

/// 18 - 基地护甲展开  param: "1"=己方,"2"=对方
constexpr int32_t BASE_ARMOR_OPEN = 18;

// ─────────────────────────────────────────────────────────────
// 仿真扩展 event_id (101+)
// 这些事件在官方协议中没有对应定义，仅用于仿真/调试
// ─────────────────────────────────────────────────────────────

// 比赛流程 (101-109)
constexpr int32_t EXT_MATCH_START = 101;  // 比赛开始  无参数
constexpr int32_t EXT_MATCH_END =
    102;  // 比赛结束  param: "red_win"/"blue_win"/"draw"
constexpr int32_t EXT_STAGE_CHANGE = 103;   // 阶段切换  param: stage_name
constexpr int32_t EXT_MATCH_PAUSED = 104;   // 比赛暂停  无参数
constexpr int32_t EXT_MATCH_RESUMED = 105;  // 比赛恢复  无参数

// 机器人生死 (110-119)
constexpr int32_t EXT_ROBOT_RESPAWN = 110;  // 机器人复活  param: robot_id
constexpr int32_t EXT_ROBOT_INSTANT_RESPAWN =
    111;  // 金币立即复活  param: "robot_id:cost"
constexpr int32_t EXT_ROBOT_OFFLINE = 112;    // 机器人离线  param: robot_id
constexpr int32_t EXT_ROBOT_RECONNECT = 113;  // 机器人重连  param: robot_id

// 前哨站/基地 (120-129)
constexpr int32_t EXT_OUTPOST_REBUILT = 120;  // 前哨站重建  param: "red"/"blue"
constexpr int32_t EXT_BASE_DESTROYED = 121;   // 基地被击毁  param: "red"/"blue"

// 能量机关 (130-139)
constexpr int32_t EXT_RUNE_BUFF_EXPIRED =
    130;  // 能量机关增益过期  param: "red"/"blue"

// 科技核心 (140-149)
constexpr int32_t EXT_TECH_CORE_ASSEMBLING =
    140;  // 开始装配  param: "team:difficulty"
constexpr int32_t EXT_TECH_CORE_ASSEMBLED =
    141;  // 装配成功  param: "team:difficulty"
constexpr int32_t EXT_TECH_CORE_FAILED =
    142;  // 装配失败  param: "team:difficulty"
constexpr int32_t EXT_TECH_CORE_LEVEL_UP =
    143;  // 等级上限提升  param: "team:max_level"

// 飞镖扩展 (150-159)
constexpr int32_t EXT_DART_LAUNCHED = 150;  // 飞镖发射  param: "team:target"
constexpr int32_t EXT_DART_GATE_CLOSE =
    151;  // 飞镖闸门关闭  param: "red"/"blue"
constexpr int32_t EXT_DART_SCREEN_BLOCKED =
    152;  // 飞镖遮挡  param: "team:duration_sec"

// 空中支援扩展 (160-169)
constexpr int32_t EXT_AIR_SUPPORT_END =
    160;  // 空中支援结束  param: "red"/"blue"
constexpr int32_t EXT_AERIAL_LOCKED =
    161;  // 空中机器人被锁定  param: "robot_id:duration"
constexpr int32_t EXT_AERIAL_LOCK_RELEASED = 162;  // 锁定解除  param: robot_id

// 判罚 (170-179)
constexpr int32_t EXT_PENALTY_YELLOW = 170;  // 黄牌  param: "robot_id:reason"
constexpr int32_t EXT_PENALTY_RED = 171;     // 红牌  param: "robot_id:reason"
constexpr int32_t EXT_PENALTY_WARNING =
    172;  // 口头警告  param: "robot_id:reason"

// 增益 (180-189)
constexpr int32_t EXT_BUFF_GAINED =
    180;  // 增益获得  param: "robot_id:buff_type:level:duration"
constexpr int32_t EXT_BUFF_EXPIRED =
    181;  // 增益过期  param: "robot_id:buff_type"
constexpr int32_t EXT_DEFENSE_ZONE_CAPTURED =
    182;  // 增益点占领  param: "robot_id:zone_type"
constexpr int32_t EXT_DEFENSE_ZONE_LOST =
    183;  // 增益点丢失  param: "robot_id:zone_type"

// 经济 (190-199)
constexpr int32_t EXT_AMMO_EXCHANGED =
    190;  // 弹药兑换  param: "robot_id:type:amount:cost"
constexpr int32_t EXT_REMOTE_HEAL = 191;  // 远程回血  param: "robot_id:cost"
constexpr int32_t EXT_GOLD_INCOME =
    192;  // 金币收入  param: "team:amount:source"
constexpr int32_t EXT_SENTRY_SUPPLY_AMMO = 193;  // 哨兵补给弹药  param: amount

// 英雄/哨兵 (200-209)
constexpr int32_t EXT_HERO_DEPLOY_EXIT = 200;  // 英雄退出部署模式
constexpr int32_t EXT_SENTRY_POSTURE_CHANGE =
    201;  // 哨兵切换姿态  param: posture_id

// 雷达 (210-219)
constexpr int32_t EXT_RADAR_MARK_THRESHOLD =
    210;  // 雷达标记达阈值  param: "target_id:progress"
constexpr int32_t EXT_RADAR_DOUBLE_VULN = 211;  // 雷达双倍易伤触发  无参数

// 等级 (220-229)
constexpr int32_t EXT_LEVEL_UP = 220;  // 等级提升  param: "robot_id:new_level"
constexpr int32_t EXT_MAX_LEVEL_REACHED =
    221;  // 达到等级上限  param: "robot_id:level"

// 底盘能量 (230-239)
constexpr int32_t EXT_ENERGY_SAVING_MODE = 230;  // 节能模式  param: robot_id
constexpr int32_t EXT_ENERGY_BOOST_MODE = 231;   // 增强模式  param: robot_id
constexpr int32_t EXT_CHASSIS_POWER_CUT = 232;   // 底盘断电  param: robot_id

// 工程特殊 (240-249)
constexpr int32_t EXT_ENGINEER_DEFENSE_START = 240;  // 工程前3分钟防御增益开始
constexpr int32_t EXT_ENGINEER_DEFENSE_END = 241;    // 工程前3分钟防御增益结束

// 特殊成就 (250-259)
constexpr int32_t EXT_FIRST_BLOOD = 250;  // 首杀  param: "killer_id:victim_id"
constexpr int32_t EXT_MULTI_KILL = 251;   // 多杀  param: "robot_id:count"
constexpr int32_t EXT_COMEBACK = 252;     // 逆转  param: "red"/"blue"

}  // namespace EventId

// ─── 增益类型枚举 (对应 Buff.buff_type) ───
namespace BuffType {
constexpr uint32_t ATTACK = 1;               // 攻击增益
constexpr uint32_t DEFENSE = 2;              // 防御增益
constexpr uint32_t HEAT_COOLDOWN = 3;        // 射击热量冷却增益
constexpr uint32_t BUFFER_ENERGY = 4;        // 缓冲能量增益
constexpr uint32_t HEAL = 5;                 // 回血增益
constexpr uint32_t VULNERABILITY = 6;        // 易伤(负防御)
constexpr uint32_t INVINCIBLE = 7;           // 无敌
constexpr uint32_t WEAK = 8;                 // 虚弱(复活后)
constexpr uint32_t SUPPLY_HEAL = 9;          // 补给区回血
constexpr uint32_t TERRAIN_CROSS = 10;       // 地形跨越增益
constexpr uint32_t RUNE_SMALL = 11;          // 小能量机关增益
constexpr uint32_t RUNE_LARGE_ATK = 12;      // 大能量机关攻击增益
constexpr uint32_t RUNE_LARGE_DEF = 13;      // 大能量机关防御增益
constexpr uint32_t RUNE_LARGE_COOL = 14;     // 大能量机关冷却增益
constexpr uint32_t ENGINEER_EARLY_DEF = 15;  // 工程前3分钟防御增益
constexpr uint32_t ASSEMBLY_DEFENSE = 16;    // 科技核心装配防御增益
constexpr uint32_t SENTRY_POSTURE = 17;      // 哨兵姿态增益
constexpr uint32_t HERO_DEPLOY = 18;         // 英雄部署模式增益
constexpr uint32_t RADAR_VULN = 19;          // 雷达标记易伤
constexpr uint32_t FORTRESS = 20;            // 堡垒增益
}  // namespace BuffType

// 辅助：根据 event_id 获取事件名称（调试用）
inline const char* eventIdToName(int32_t id) {
  switch (id) {
    // 官方 1-18
    case EventId::KILL:
      return "击杀";
    case EventId::STRUCTURE_DESTROYED:
      return "建筑摧毁";
    case EventId::RUNE_CHANCE_CHANGE:
      return "能量机关次数变化";
    case EventId::RUNE_CAN_ACTIVATE:
      return "能量机关可激活";
    case EventId::RUNE_ARM_RESULT:
      return "能量机关臂数结果";
    case EventId::RUNE_ACTIVATED:
      return "能量机关被激活";
    case EventId::HERO_DEPLOY_MODE:
      return "英雄部署模式";
    case EventId::HERO_SNIPER_OWN:
      return "己方英雄狙击";
    case EventId::HERO_SNIPER_ENEMY:
      return "对方英雄狙击";
    case EventId::AIR_SUPPORT_OWN:
      return "己方空中支援";
    case EventId::AIR_SUPPORT_OWN_BROKEN:
      return "己方空中支援被打断";
    case EventId::AIR_SUPPORT_ENEMY:
      return "对方空中支援";
    case EventId::AIR_SUPPORT_ENEMY_BROKEN:
      return "对方空中支援被打断";
    case EventId::DART_HIT:
      return "飞镖命中";
    case EventId::DART_GATE:
      return "飞镖闸门";
    case EventId::BASE_ATTACKED:
      return "基地遭攻击";
    case EventId::OUTPOST_STOP_ROTATE:
      return "前哨站停转";
    case EventId::BASE_ARMOR_OPEN:
      return "基地护甲展开";
    // 扩展 101+
    case EventId::EXT_MATCH_START:
      return "比赛开始";
    case EventId::EXT_MATCH_END:
      return "比赛结束";
    case EventId::EXT_STAGE_CHANGE:
      return "阶段切换";
    case EventId::EXT_ROBOT_RESPAWN:
      return "机器人复活";
    case EventId::EXT_ROBOT_INSTANT_RESPAWN:
      return "立即复活";
    case EventId::EXT_TECH_CORE_ASSEMBLED:
      return "科技核心装配成功";
    case EventId::EXT_LEVEL_UP:
      return "等级提升";
    case EventId::EXT_FIRST_BLOOD:
      return "首杀";
    default:
      return "未知事件";
  }
}

#endif  // MOCKSERVERCPP_EVENT_TYPES_H_
