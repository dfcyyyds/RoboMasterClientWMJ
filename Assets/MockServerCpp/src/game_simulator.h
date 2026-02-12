#ifndef MOCKSERVERCPP_GAME_SIMULATOR_H_
#define MOCKSERVERCPP_GAME_SIMULATOR_H_

#include <array>
#include <cstdint>
#include <cstring>
#include <random>
#include <string>
#include <utility>
#include <vector>

#include "RoboMasterClientMessage.pb.h"
#include "event_types.h"

// ═══════════════════════════════════════════════════════════════
// RoboMaster 2026 比赛仿真器 V2.0
// 根据《RoboMaster 2026 机甲大师超级对抗赛比赛规则 V1.3.0》
// 精确还原比赛各子系统数据
// ═══════════════════════════════════════════════════════════════

// ─── 常量定义 ───

constexpr float MATCH_DURATION = 420.0f;
constexpr float PREP_DURATION = 180.0f;
constexpr float SELFCHECK_DURATION = 15.0f;
constexpr float COUNTDOWN_DURATION = 5.0f;

// 快速模式：用于测试，几秒内进入比赛
constexpr float FAST_PREP_DURATION = 3.0f;
constexpr float FAST_SELFCHECK_DURATION = 1.0f;
constexpr float FAST_COUNTDOWN_DURATION = 2.0f;
constexpr float FAST_MATCH_DURATION = 300.0f;

constexpr uint32_t BASE_MAX_HEALTH = 5000;
constexpr uint32_t OUTPOST_MAX_HEALTH = 1500;
constexpr uint32_t OUTPOST_REBUILD_HEALTH = 750;
constexpr uint32_t BASE_ARMOR_OPEN_THRESHOLD = 2000;

constexpr uint32_t INITIAL_GOLD = 400;
constexpr uint32_t PERIODIC_GOLD = 50;
constexpr uint32_t FINAL_GOLD = 150;

// 弹药兑换 (非远程)
constexpr uint32_t AMMO_17MM_COST = 10;
constexpr uint32_t AMMO_17MM_AMOUNT = 10;
constexpr uint32_t AMMO_42MM_COST = 10;
constexpr uint32_t AMMO_42MM_AMOUNT = 1;
constexpr uint32_t AMMO_17MM_TEAM_CAP = 1000;
constexpr uint32_t AMMO_42MM_TEAM_CAP = 100;

constexpr int NUM_ROBOTS = 5;

constexpr uint32_t BUFFER_ENERGY_MAX = 60;
constexpr uint32_t CHASSIS_ENERGY_INITIAL = 20000;
constexpr uint32_t CHASSIS_ENERGY_MAX = 40000;
constexpr uint32_t CHASSIS_ENERGY_BOOST_THRESHOLD = 25000;
constexpr uint32_t CHASSIS_POWER_SAVING_LIMIT = 35;
constexpr uint32_t CHASSIS_POWER_BOOST_MAX = 200;

constexpr float ARENA_WIDTH = 28.0f;
constexpr float ARENA_HEIGHT = 15.0f;

// 伤害
constexpr uint32_t DMG_17MM = 20;
constexpr uint32_t DMG_42MM = 200;
constexpr uint32_t DMG_COLLISION = 2;
constexpr float HEAT_PER_17MM = 10.0f;
constexpr float HEAT_PER_42MM = 100.0f;

// 脱战 & 补给区
constexpr float OUT_OF_COMBAT_TIME = 6.0f;
constexpr float SUPPLY_HEAL_RATE = 0.10f;
constexpr float SUPPLY_HEAL_RATE_LATE = 0.25f;
constexpr float SUPPLY_HEAL_LATE_TIME = 240.0f;
constexpr float REMOTE_HEAL_PERCENT = 0.60f;
constexpr float REMOTE_HEAL_DELAY = 6.0f;

// 复活
constexpr float RESPAWN_INVINCIBLE_NORMAL = 30.0f;
constexpr float RESPAWN_INVINCIBLE_INSTANT = 3.0f;
constexpr float RESPAWN_HEALTH_PERCENT = 0.10f;
constexpr float RESPAWN_POWER_BOOST_DURATION = 4.0f;

// 哨兵
constexpr uint32_t SENTRY_AUTO_HEALTH = 400;
constexpr uint32_t SENTRY_SEMI_HEALTH = 200;
constexpr uint32_t SENTRY_AUTO_HEAT = 260;
constexpr uint32_t SENTRY_SEMI_HEAT = 100;
constexpr float SENTRY_AUTO_COOLDOWN = 30.0f;
constexpr float SENTRY_SEMI_COOLDOWN = 10.0f;
constexpr uint32_t SENTRY_AUTO_POWER = 100;
constexpr uint32_t SENTRY_SEMI_POWER = 60;
constexpr uint32_t SENTRY_INITIAL_AMMO = 300;
constexpr uint32_t SENTRY_SUPPLY_AMMO = 100;
constexpr uint32_t SENTRY_AUTO_CTRL_COST = 50;
constexpr float SENTRY_POSTURE_WEAKEN_TIME = 180.0f;
constexpr float SENTRY_POSTURE_COOLDOWN = 5.0f;

// 英雄吊射
constexpr uint32_t LOBSHOT_VISION_W = 192;
constexpr uint32_t LOBSHOT_VISION_H = 144;
constexpr uint32_t LOBSHOT_FRAME_BYTES =
    LOBSHOT_VISION_W * LOBSHOT_VISION_H / 8;                 // 3456B
constexpr uint32_t LOBSHOT_BLOCKS_X = LOBSHOT_VISION_W / 8;  // 24
constexpr uint32_t LOBSHOT_BLOCKS_Y = LOBSHOT_VISION_H / 8;  // 18
constexpr uint32_t LOBSHOT_TOTAL_BLOCKS =
    LOBSHOT_BLOCKS_X * LOBSHOT_BLOCKS_Y;       // 432
constexpr float LOBSHOT_FLIGHT_TIME = 1.2f;    // 弹丸飞行时间(秒)
constexpr float LOBSHOT_42MM_INTERVAL = 2.0f;  // 42mm射击间隔(秒)
constexpr uint32_t LOBSHOT_TARGET_CX = 96;     // 靶标中心X
constexpr uint32_t LOBSHOT_TARGET_CY = 60;     // 靶标中心Y
constexpr uint32_t LOBSHOT_TARGET_R = 3;       // 靶标半径(小圆)

// 50Hz调度器常量 (严格按 §6 / §11 规范)
constexpr int SCHEDULER_HZ = 50;               // 总slot频率 50Hz
constexpr int I_FRAME_INTERVAL = 25;           // 每25个视频帧发一次I帧
constexpr int TRAIL_INTERVAL = 8;              // 每8个视频帧发一次独立轨迹帧
constexpr uint16_t MAX_LOBSHOT_PAYLOAD = 600;  // 载荷上限

// 帧类型常量 (§4.3)
constexpr uint8_t FT_I_PART1 = 0x01;
constexpr uint8_t FT_I_PART2 = 0x02;
constexpr uint8_t FT_I_SINGLE = 0x03;
constexpr uint8_t FT_D_FRAME = 0x10;
constexpr uint8_t FT_D_EMPTY = 0x11;
constexpr uint8_t FT_TRAIL = 0x20;
constexpr uint8_t FT_HEARTBEAT = 0xFE;

// 包标记
constexpr uint8_t SYNC_BYTE = 0xA5;
constexpr uint8_t END_BYTE = 0x5A;

// 工程
constexpr uint32_t ENGINEER_HEALTH = 250;
constexpr uint32_t ENGINEER_POWER = 120;
constexpr float ENGINEER_EARLY_DEFENSE_TIME = 180.0f;

// 空中支援
constexpr float AIR_SUPPORT_INITIAL_TIME = 30.0f;
constexpr float AIR_SUPPORT_PERIODIC = 20.0f;
constexpr uint32_t AIR_SUPPORT_AMMO = 750;
constexpr float AIR_SUPPORT_COST_PER_SEC = 1.0f;
constexpr float AIR_LOCK_DURATION = 45.0f;
constexpr uint32_t AIR_LOCK_MAX = 3;

// 飞镖
constexpr float DART_GATE_OPEN_TIME_1 = 30.0f;
constexpr float DART_GATE_OPEN_TIME_2 = 240.0f;
constexpr float DART_GATE_OPEN_DURATION = 7.0f;
constexpr float DART_FIRE_WINDOW = 30.0f;
constexpr float DART_COOLDOWN = 15.0f;
constexpr uint32_t DART_PER_MATCH = 4;
constexpr uint32_t DART_DMG_FIXED = 200;
constexpr uint32_t DART_DMG_RANDOM_FIXED = 300;
constexpr uint32_t DART_DMG_RANDOM_MOVING = 625;
constexpr uint32_t DART_DMG_END_MOVING = 1000;
constexpr uint32_t DART_DMG_OUTPOST = 750;
constexpr uint32_t DART_EXP_FIXED = 200;
constexpr uint32_t DART_EXP_RANDOM_FIXED = 600;
constexpr uint32_t DART_EXP_RANDOM_MOVING = 2500;
constexpr float DART_BLOCK_MOVING = 10.0f;

// 能量机关
constexpr float RUNE_ACTIVATE_WINDOW = 20.0f;
constexpr float RUNE_SMALL_DEFENSE = 0.25f;
constexpr float RUNE_SMALL_DURATION = 45.0f;
constexpr float RUNE_SMALL_EXP_BONUS = 1.0f;
constexpr uint32_t RUNE_SMALL_EXP_CAP = 1200;
constexpr uint32_t RUNE_LARGE_EXP_TOTAL = 750;

// 雷达
constexpr float RADAR_P_VULN_1 = 100.0f;
constexpr float RADAR_P_VULN_2 = 120.0f;
constexpr float RADAR_P_MAX = 150.0f;
constexpr float RADAR_VULN_1_PERCENT = 0.15f;
constexpr float RADAR_VULN_2_PERCENT = 0.20f;
constexpr float RADAR_DOUBLE_VULN_ACCUMULATE = 60.0f;
constexpr float RADAR_DOUBLE_VULN_DURATION = 30.0f;
constexpr uint32_t RADAR_DOUBLE_VULN_MAX = 2;

// 堡垒
constexpr float FORTRESS_DEFENSE = 0.50f;
constexpr float FORTRESS_ENEMY_VULN = 1.00f;
constexpr float FORTRESS_ENEMY_TIME = 180.0f;
constexpr float FORTRESS_ENEMY_CAP_DURATION = 20.0f;

// 科技核心解锁时间(比赛经过秒数)
constexpr float TECH_LEVEL2_UNLOCK = 60.0f;
constexpr float TECH_LEVEL3_UNLOCK = 120.0f;
constexpr float TECH_LEVEL4_UNLOCK = 180.0f;

// 前哨站
constexpr uint32_t OUTPOST_REBUILD_PER_LOSS = 1000;
constexpr float OUTPOST_REBUILD_CUTOFF = 300.0f;
constexpr float OUTPOST_ARMOR_STOP_TIME = 180.0f;

// 飞镖遮挡递减表 (固定/随机固定目标, 1-4次)
static const float DART_BLOCK_TIMES[] = {10.0f, 5.0f, 3.0f, 2.0f};

// ─── 枚举 ───

enum class GameStage : uint32_t {
  NotStarted = 0,
  Preparation = 1,
  SelfCheck = 2,
  Countdown = 3,
  InProgress = 4,
  Ended = 5
};

enum class RobotType : uint32_t {
  Hero = 1,
  Engineer = 2,
  Infantry3 = 3,
  Infantry4 = 4,
  Sentry = 7
};

enum class SentryPosture : uint32_t { Attack = 0, Defense = 1, Move = 2 };
enum class SentryControl : uint32_t { Auto = 0, SemiAuto = 1 };
enum class UnitStatus : uint32_t {
  Normal = 0,
  Invincible = 1,
  ArmorOpen = 2,
  Destroyed = 3
};
enum class RunePhase : uint32_t {
  Inactive = 0,
  Activating = 1,
  Activated = 2,
  Failed = 3
};
enum class RuneSize : uint32_t { Small = 0, Large = 1 };
enum class DartTarget : uint32_t {
  None = 0,
  Outpost = 1,
  BaseFixed = 2,
  BaseRandomFixed = 3,
  BaseRandomMoving = 4,
  BaseEndMoving = 5
};
enum class DartGateState : uint32_t { Closed = 0, Opening = 1, Open = 2 };

// ─── 性能属性表 ───

struct PerformanceStats {
  uint32_t max_health, max_power, max_heat;
  float heat_cooldown_rate;
};

static const PerformanceStats HERO_STATS[2][10] = {{{200, 70, 140, 12},
                                                    {225, 75, 150, 14},
                                                    {250, 80, 160, 16},
                                                    {275, 85, 170, 18},
                                                    {300, 90, 180, 20},
                                                    {325, 95, 190, 22},
                                                    {350, 100, 200, 24},
                                                    {375, 105, 210, 26},
                                                    {400, 110, 220, 28},
                                                    {450, 120, 240, 30}},
                                                   {{150, 50, 100, 20},
                                                    {165, 55, 102, 23},
                                                    {180, 60, 104, 26},
                                                    {195, 65, 106, 29},
                                                    {210, 70, 108, 32},
                                                    {225, 75, 110, 35},
                                                    {240, 80, 115, 38},
                                                    {255, 85, 120, 41},
                                                    {270, 90, 125, 44},
                                                    {300, 100, 130, 50}}};

struct ChassisStats {
  uint32_t max_health, max_power;
};
static const ChassisStats INFANTRY_CHASSIS[2][10] = {{{150, 60},
                                                      {175, 65},
                                                      {200, 70},
                                                      {225, 75},
                                                      {250, 80},
                                                      {275, 85},
                                                      {300, 90},
                                                      {325, 95},
                                                      {350, 100},
                                                      {400, 100}},
                                                     {{200, 45},
                                                      {225, 50},
                                                      {250, 55},
                                                      {275, 60},
                                                      {300, 65},
                                                      {325, 70},
                                                      {350, 75},
                                                      {375, 80},
                                                      {400, 90},
                                                      {400, 100}}};

struct ShooterStats {
  uint32_t max_heat;
  float heat_cooldown_rate;
};
static const ShooterStats INFANTRY_SHOOTER[2][10] = {{{170, 5},
                                                      {180, 7},
                                                      {190, 9},
                                                      {200, 11},
                                                      {210, 12},
                                                      {220, 13},
                                                      {230, 14},
                                                      {240, 16},
                                                      {250, 18},
                                                      {260, 20}},
                                                     {{40, 12},
                                                      {48, 14},
                                                      {56, 16},
                                                      {64, 18},
                                                      {72, 20},
                                                      {80, 22},
                                                      {88, 24},
                                                      {96, 26},
                                                      {114, 28},
                                                      {120, 30}}};

static const ShooterStats AERIAL_SHOOTER[10] = {
    {100, 20}, {110, 30}, {120, 40}, {130, 50},  {140, 60},
    {150, 70}, {160, 80}, {170, 90}, {180, 100}, {200, 120}};

static const uint32_t EXP_TABLE[10] = {0,    550,  1100, 1650, 2200,
                                       2750, 3300, 3850, 4400, 5000};

// 大能量机关增益表(表5-18)
struct LargeRuneBuff {
  float attack_bonus, defense_bonus, cooldown_mult;
};
static const LargeRuneBuff LARGE_RUNE_BUFFS[5] = {{1.5f, 0.25f, 1.0f},
                                                  {1.5f, 0.25f, 2.0f},
                                                  {2.0f, 0.25f, 2.0f},
                                                  {2.0f, 0.25f, 3.0f},
                                                  {3.0f, 0.50f, 5.0f}};
// 大能量机关灯臂数→持续时间(表5-19)
static const float LARGE_RUNE_DURATION[6] = {30.0f, 35.0f, 40.0f,
                                             45.0f, 50.0f, 60.0f};

// 定时金币发放 (比赛经过秒数)
static const float GOLD_ELAPSED_TIMES[] = {1.0f,   61.0f,  121.0f, 181.0f,
                                           241.0f, 301.0f, 361.0f};
static const uint32_t GOLD_AMOUNTS[] = {0, 50, 50, 50, 50, 50, 150};
constexpr int GOLD_TICK_COUNT = 7;

// 科技核心装配收益
struct AssemblyReward {
  uint32_t first_gold_per_10s, extra_gold_per_10s, max_level_unlock;
  float defense_bonus;
  uint32_t base_health_bonus;
};
static const AssemblyReward ASSEMBLY_REWARDS[4] = {{50, 5, 0, 0.00f, 0},
                                                   {25, 10, 7, 0.00f, 0},
                                                   {25, 15, 10, 0.25f, 0},
                                                   {50, 0, 0, 0.50f, 2000}};

// ─── 状态结构 ───

struct BuffState {
  uint32_t buff_type = 0;
  int32_t buff_level = 0;
  float max_time = 0;
  float left_time = 0;
};

struct RadarMarkState {
  float progress = 0;
  float x_value = 0;
  bool last_was_positive = false;
  float vuln_accumulated = 0;
};

struct DartState {
  DartGateState gate_state = DartGateState::Closed;
  float gate_timer = 0;
  float fire_window_timer = 0;
  float cooldown_timer = 0;
  uint32_t gate_opens_remaining = 2;
  uint32_t darts_remaining = DART_PER_MATCH;
  uint32_t darts_hit_count = 0;
  DartTarget current_target = DartTarget::None;
  bool first_gate_used = false;
  float screen_block_timer = 0;
  bool gate_open_1_given = false;
  bool gate_open_2_given = false;
};

struct RuneState {
  RunePhase phase = RunePhase::Inactive;
  RuneSize current_size = RuneSize::Small;
  float timer = 0;
  float buff_remaining = 0;
  uint32_t small_chances = 0;
  uint32_t large_chances = 0;
  uint32_t activated_arms = 0;
  float average_rings = 0;
  uint32_t small_rune_bonus_exp_used = 0;
  float attack_bonus = 0;
  float defense_bonus = 0;
  float cooldown_mult = 1.0f;
  // 时间门控
  bool small_t0_given = false;    // 比赛开始时
  bool small_t90_given = false;   // 1分30秒时
  bool large_t180_given = false;  // 3分钟时
  bool large_t255_given = false;  // 4分15秒时
  bool large_t330_given = false;  // 5分30秒时
};

struct FortressState {
  bool activated = false;
  bool ally_occupying = false;
  bool enemy_occupying = false;
  float enemy_occupy_timer = 0;
  float enemy_occupy_decay = 0;
  bool enemy_cap_triggered = false;
  float heat_cooldown_bonus = 0;
  uint32_t reserve_ammo_cap = 0;
  uint32_t reserve_ammo = 0;
};

struct RobotState {
  RobotType type;
  uint32_t robot_id = 0;
  uint32_t shooter_type = 1;
  uint32_t chassis_type = 1;
  SentryControl sentry_ctrl = SentryControl::Auto;

  bool is_connected = true;
  bool is_alive = true;
  bool is_on_field = true;

  uint32_t level = 1;
  uint32_t experience = 0;
  uint32_t max_level = 5;

  uint32_t max_health = 200;
  uint32_t current_health = 200;

  uint32_t max_heat = 100;
  float current_heat = 0;
  float heat_cooldown_rate = 12;
  float base_cooldown_rate = 12;

  uint32_t max_power = 50;
  uint32_t base_power = 50;
  uint32_t max_buffer_energy = BUFFER_ENERGY_MAX;
  uint32_t current_buffer_energy = BUFFER_ENERGY_MAX;
  uint32_t max_chassis_energy = CHASSIS_ENERGY_MAX;
  uint32_t current_chassis_energy = CHASSIS_ENERGY_INITIAL;
  bool power_cut = false;
  float power_cut_timer = 0;
  bool energy_saving = false;
  bool energy_boost = false;

  int32_t remaining_ammo = 0;
  uint32_t total_projectiles_fired = 0;

  bool is_out_of_combat = false;
  float no_fire_timer = 0;
  float no_damage_timer = 0;

  bool in_supply_zone = false;

  bool remote_heal_pending = false;
  float remote_heal_timer = 0;

  uint32_t total_damage = 0;
  uint32_t collision_damage = 0;
  uint32_t small_projectile_damage = 0;
  uint32_t large_projectile_damage = 0;
  uint32_t dart_splash_damage = 0;
  uint32_t module_offline_damage = 0;
  uint32_t offline_damage = 0;
  uint32_t penalty_damage = 0;
  uint32_t server_kill_damage = 0;
  uint32_t killer_id = 0;

  bool is_pending_respawn = false;
  float respawn_progress = 0;
  float total_respawn_progress = 10;
  uint32_t instant_respawn_count = 0;
  bool in_base_area = false;

  float pos_x = 0, pos_y = 0, pos_z = 0, yaw = 0;
  float target_x = 0, target_y = 0;
  float move_timer = 0;

  std::vector<BuffState> buffs;

  uint32_t mod_power_manager = 1, mod_rfid = 1, mod_light_strip = 1;
  uint32_t mod_small_shooter = 1, mod_big_shooter = 1, mod_uwb = 1;
  uint32_t mod_armor = 1, mod_video = 1, mod_capacitor = 1;
  uint32_t mod_main_controller = 1, mod_laser_detection = 1;

  SentryPosture posture = SentryPosture::Move;
  float posture_attack_accum = 0;
  float posture_defense_accum = 0;
  float posture_move_accum = 0;
  float posture_switch_cooldown = 0;
  bool posture_weakened = false;

  bool is_deployed = false;

  // 英雄吊射模式
  bool lobshot_active = false;
  float lobshot_fire_cooldown = 0;
  float lobshot_ball_time = -1;  // <0表示无弹丸飞行中
  float lobshot_ball_x = 0;
  float lobshot_ball_y = 0;
  uint16_t lobshot_frame_id = 0;

  // 50Hz调度器状态 (严格按§6规范)
  int lobshot_slot_counter = 0;         // 总slot计数(0-based, 50Hz递增)
  int lobshot_video_frame_counter = 0;  // 视频帧计数(仅偶数slot递增)
  uint8_t lobshot_prev_frame[LOBSHOT_FRAME_BYTES] =
      {};                              // 前一帧缓冲(用于XOR差分)
  bool lobshot_prev_valid = false;     // prev_frame是否有效
  bool lobshot_part2_pending = false;  // I帧Part2待发标志

  // 弹丸轨迹累积 (I帧周期内持续累积, 仿真简化为持续保留最近N点)
  static constexpr int LOBSHOT_MAX_TRAIL = 120;
  struct TrailPt {
    uint8_t x;
    uint8_t y;
    uint8_t r;  // 投影半径(近大远小, 仅服务端渲染用)
  };
  std::vector<TrailPt> lobshot_trail;  // 累积轨迹点

  float fire_interval = 0;
  float last_fire_speed = 0;
  uint32_t kill_count = 0;

  float invincible_timer = 0;
  bool is_weak = false;
  float power_boost_timer = 0;

  float eff_defense = 0;
  float eff_attack = 1.0f;
  float eff_cooldown_mult = 1.0f;

  RadarMarkState radar_mark;
};

struct TeamState {
  uint32_t base_health = BASE_MAX_HEALTH;
  uint32_t base_shield = 0;
  UnitStatus base_status = UnitStatus::Invincible;
  uint32_t base_damage_total = 0;

  uint32_t outpost_health = OUTPOST_MAX_HEALTH;
  UnitStatus outpost_status = UnitStatus::Normal;
  bool outpost_ever_destroyed = false;
  bool outpost_armor_rotating = true;
  uint32_t outpost_rebuild_chances = 0;
  uint32_t outpost_rebuild_used_damage = 0;

  uint32_t remaining_economy = INITIAL_GOLD;
  uint64_t total_economy_obtained = INITIAL_GOLD;

  uint32_t total_17mm_exchanged = 0;
  uint32_t total_42mm_exchanged = 0;

  uint32_t tech_level = 0;
  uint32_t encryption_level = 0;
  int assembly_completions[4] = {0, 0, 0, 0};
  uint32_t assembly_gold_per_10s = 0;
  float assembly_defense_bonus = 0;
  uint32_t team_max_level = 5;
  bool level4_completed = false;
  bool level4_failed = false;
  float assembly_timer = 0;
  bool is_assembling = false;
  uint32_t assembling_level = 0;

  FortressState fortress;
  RobotState robots[NUM_ROBOTS];
  uint32_t total_damage_dealt = 0;

  float air_support_time_total = AIR_SUPPORT_INITIAL_TIME;
  float air_support_time_remaining = AIR_SUPPORT_INITIAL_TIME;
  bool air_support_active = false;
  uint32_t air_support_cost_total = 0;
  uint32_t air_support_ammo_remaining = AIR_SUPPORT_AMMO;

  DartState dart;
  RuneState rune;

  float radar_vuln_accumulate = 0;
  uint32_t radar_double_vuln_used = 0;
  bool radar_double_vuln_active = false;
  float radar_double_vuln_timer = 0;

  float sentry_supply_timer = 0;
  uint32_t sentry_supply_pending = 0;
};

struct GameEvent {
  int32_t event_id;
  std::string param;
};

// ═══════════════════════════════════════════════════════════════
// GameSimulator
// ═══════════════════════════════════════════════════════════════

class GameSimulator {
 public:
  GameSimulator();
  void init(int self_robot_index = 2, bool fast_mode = true);
  void tick(float dt);
  bool isFastMode() const { return fast_mode_; }

  std::vector<uint8_t> buildGameStatus();
  std::vector<uint8_t> buildGlobalUnitStatus();
  std::vector<uint8_t> buildGlobalLogisticsStatus();
  std::vector<uint8_t> buildGlobalSpecialMechanism();
  std::vector<uint8_t> buildEvent();
  std::vector<uint8_t> buildRobotInjuryStat();
  std::vector<uint8_t> buildRobotRespawnStatus();
  std::vector<uint8_t> buildRobotStaticStatus();
  std::vector<uint8_t> buildRobotDynamicStatus();
  std::vector<uint8_t> buildRobotModuleStatus();
  std::vector<uint8_t> buildRobotPosition();
  std::vector<uint8_t> buildBuff();
  std::vector<uint8_t> buildAllBuffs();  // 发送所有buff（逐个轮流）
  std::vector<uint8_t> buildPenaltyInfo();
  std::vector<uint8_t> buildRobotPathPlanInfo();
  std::vector<uint8_t> buildRadarInfoToClient();
  std::vector<uint8_t> buildTechCoreMotionStateSync();
  std::vector<uint8_t> buildRobotPerformanceSelectionSync();
  std::vector<uint8_t> buildDeployModeStatusSync();
  std::vector<uint8_t> buildRuneStatusSync();
  std::vector<uint8_t> buildSentryStatusSync();
  std::vector<uint8_t> buildDartSelectTargetStatusSync();
  std::vector<uint8_t> buildSentryCtrlResult();
  std::vector<uint8_t> buildAirSupportStatusSync();
  std::vector<uint8_t> buildMessageForTopic(const std::string& topic);

  bool hasEvents() const { return !pending_events_.empty(); }
  GameStage getStage() const { return stage_; }

  // ─── 客户端指令处理 ───
  /// 处理射击指令（由客户端 CommonCommand cmd_type=100 触发）
  void handleFireCommand(int count = 1, float dt = 1.0f);
  /// 处理弹药购买指令（由客户端 CommonCommand cmd_type=101 触发）
  void handleAmmoPurchase(uint32_t batches);
  /// 处理买活指令（由客户端 CommonCommand cmd_type=102 触发）
  void handleBuybackCommand();
  /// 处理英雄吊射部署指令（cmd_type=103: 进入, cmd_type=104: 退出）
  void handleDeployCommand(bool enter);
  /// 处理吊射模式下射击（cmd_type=105）
  void handleLobShotFire();
  /// 是否处于吊射模式（用于决定是否发送 CustomByteBlock）
  bool isLobShotActive() const;
  /// 50Hz调度器tick — 每20ms调用一次, 返回要发送的CustomByteBlock数据
  /// 偶数slot: 视频帧(I/D/Trail), 奇数slot: 冗余(Part2/心跳)
  std::vector<uint8_t> tickLobShotSlot();

 private:
  void initRobot(RobotState& r, RobotType type, uint32_t id, float sx,
                 float sy);
  void updatePerformanceStats(RobotState& r);
  void recalcEffectiveStats(RobotState& r, TeamState& team);
  uint32_t getLevelForExp(uint32_t exp, uint32_t max_level);

  void tickStageTransition();
  void tickEconomy(float dt);
  void tickAmmoExchange(float dt);
  void tickCombat(float dt);
  void tickOutpostBaseDamage(float dt);
  void tickHeatCooldown(float dt);
  void tickRespawn(float dt);
  void tickBuffs(float dt);
  void tickOutOfCombat(float dt);
  void tickSupplyZone(float dt);
  void tickRemoteHeal(float dt);
  void tickPositions(float dt);
  void tickExperience(float dt);
  void tickAirSupport(float dt);
  void tickRune(float dt);
  void tickOutpost(float dt);
  void tickSentryPosture(float dt);
  void tickSentrySupply(float dt);
  void tickEngineerDefense(float dt);
  void tickAssembly(float dt);
  void tickFortress(float dt);
  void tickRadarMark(float dt);
  void tickDart(float dt);
  void tickChassisEnergy(float dt);
  void tickBuffEffects();

  void dealDamage(RobotState& target, TeamState& target_team, uint32_t damage,
                  uint32_t attacker_id, bool is_17mm, bool is_dart = false);
  void dealBaseDamage(TeamState& team, uint32_t damage);
  void dealOutpostDamage(TeamState& team, uint32_t damage);
  void killRobot(RobotState& r, uint32_t killer_id);
  void respawnRobot(RobotState& r, bool instant);
  uint32_t calcRespawnGoldCost(const RobotState& r) const;
  float calcRemoteHealCost() const;
  void addBuff(RobotState& r, uint32_t type, int32_t level, float duration);
  void removeBuff(RobotState& r, uint32_t type);
  bool hasBuff(const RobotState& r, uint32_t type) const;
  void pushEvent(int32_t event_id, const std::string& param = "");

  template <typename T>
  std::vector<uint8_t> serialize(const T& msg);

  bool fast_mode_ = true;
  uint32_t buff_send_index_ = 0;  // 轮流发送buff

  GameStage stage_ = GameStage::NotStarted;
  float stage_timer_ = 0;
  float match_elapsed_ = 0;
  float total_elapsed_ = 0;

  uint32_t current_round_ = 1, total_rounds_ = 3;
  uint32_t red_score_ = 0, blue_score_ = 0;

  TeamState ally_, enemy_;
  int self_robot_idx_ = 2;

  float economy_10s_timer_ = 0;
  int gold_tick_index_ = 0;
  float ammo_exchange_timer_ = 0;

  // 玩家射击冷却计时（防止超速射击）
  float player_fire_cooldown_ = 0;

  std::vector<GameEvent> pending_events_;
  bool first_blood_happened_ = false;

  std::mt19937 rng_;
  float randFloat(float min, float max);
  int randInt(int min, int max);
  bool randChance(float probability);
};

#endif  // MOCKSERVERCPP_GAME_SIMULATOR_H_
