#include "protocol.h"

#include <chrono>
#include <ctime>
#include <iostream>
#include <random>
#include <string>
#include <type_traits>
#include <utility>

#include "game_simulator.h"

namespace {
// 真随机数引擎（基于时间种子）
static std::mt19937 rng(static_cast<unsigned>(
    std::chrono::system_clock::now().time_since_epoch().count()));

// 随机生成字符串
template <size_t N = 8>
std::string rand_str() {
  static const char alphanum[] =
      "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
  std::uniform_int_distribution<> dist(0, sizeof(alphanum) - 2);
  std::string s;
  for (size_t i = 0; i < N; ++i) s += alphanum[dist(rng)];
  return s;
}
// 随机生成bytes
template <size_t N = 8>
std::string rand_bytes() {
  std::uniform_int_distribution<> dist(0, 255);
  std::string s;
  for (size_t i = 0; i < N; ++i) s += static_cast<char>(dist(rng));
  return s;
}
// 随机bool
auto rand_bool = []() {
  std::uniform_int_distribution<> dist(0, 1);
  return dist(rng) == 0;
};
// 随机float
auto rand_float = [](float min, float max) {
  std::uniform_real_distribution<float> dist(min, max);
  return dist(rng);
};
// 随机int
auto rand_int = [](int min, int max) {
  std::uniform_int_distribution<int> dist(min, max);
  return dist(rng);
};
// 随机uint
auto rand_uint = [](unsigned min, unsigned max) {
  std::uniform_int_distribution<unsigned> dist(min, max);
  return dist(rng);
};

// 仿真器实例
static GameSimulator g_sim;
}  // namespace

void init_simulator(int self_robot_index, bool fast_mode) {
  g_sim.init(self_robot_index, fast_mode);
}

void tick_simulator(float dt) { g_sim.tick(dt); }

std::pair<std::string, std::vector<uint8_t>> build_simulated_message(
    const std::string& type) {
  auto payload = g_sim.buildMessageForTopic(type);
  return {type, payload};
}

// 支持指定类型生成
std::pair<std::string, std::vector<uint8_t>> build_random_message(
    const std::string& type) {
  std::vector<uint8_t> buf;

  if (type == "KeyboardMouseControl") {
    KeyboardMouseControl msg;
    msg.set_mouse_x(rand_int(-1000, 1000));
    msg.set_mouse_y(rand_int(-1000, 1000));
    msg.set_mouse_z(rand_int(-1000, 1000));
    msg.set_left_button_down(rand_bool());
    msg.set_right_button_down(rand_bool());
    msg.set_keyboard_value(rand_uint(0, 0xFFFF));
    msg.set_mid_button_down(rand_bool());
    buf.resize(msg.ByteSizeLong());
    msg.SerializeToArray(buf.data(), buf.size());
  } else if (type == "CustomControl") {
    CustomControl msg;
    msg.set_data(rand_bytes<30>());
    buf.resize(msg.ByteSizeLong());
    msg.SerializeToArray(buf.data(), buf.size());
  } else if (type == "GameStatus") {
    GameStatus msg;
    msg.set_current_round(rand_uint(1, 10));
    msg.set_total_rounds(rand_uint(5, 20));
    msg.set_red_score(rand_uint(0, 1000));
    msg.set_blue_score(rand_uint(0, 1000));
    msg.set_current_stage(rand_uint(0, 5));
    msg.set_stage_countdown_sec(rand_int(-60, 600));
    msg.set_stage_elapsed_sec(rand_int(0, 600));
    msg.set_is_paused(rand_bool());
    buf.resize(msg.ByteSizeLong());
    msg.SerializeToArray(buf.data(), buf.size());
  } else if (type == "GlobalUnitStatus") {
    GlobalUnitStatus msg;
    msg.set_base_health(rand_uint(0, 10000));
    msg.set_base_status(rand_uint(0, 3));
    msg.set_base_shield(rand_uint(0, 1000));
    msg.set_outpost_health(rand_uint(0, 10000));
    msg.set_outpost_status(rand_uint(0, 3));
    msg.set_enemy_base_health(rand_uint(0, 10000));
    msg.set_enemy_base_status(rand_uint(0, 3));
    msg.set_enemy_base_shield(rand_uint(0, 1000));
    msg.set_enemy_outpost_health(rand_uint(0, 10000));
    msg.set_enemy_outpost_status(rand_uint(0, 3));
    for (int i = 0; i < 5; ++i) msg.add_robot_health(rand_uint(0, 1000));
    for (int i = 0; i < 5; ++i) msg.add_robot_bullets(rand_int(0, 500));
    msg.set_total_damage_ally(rand_uint(0, 10000));
    msg.set_total_damage_enemy(rand_uint(0, 10000));
    buf.resize(msg.ByteSizeLong());
    msg.SerializeToArray(buf.data(), buf.size());
  } else if (type == "GlobalLogisticsStatus") {
    GlobalLogisticsStatus msg;
    msg.set_remaining_economy(rand_uint(0, 10000));
    msg.set_total_economy_obtained(rand_uint(0, 1000000));
    msg.set_tech_level(rand_uint(0, 10));
    msg.set_encryption_level(rand_uint(0, 5));
    buf.resize(msg.ByteSizeLong());
    msg.SerializeToArray(buf.data(), buf.size());
  } else if (type == "GlobalSpecialMechanism") {
    GlobalSpecialMechanism msg;
    for (int i = 0; i < 3; ++i) msg.add_mechanism_id(rand_uint(0, 100));
    for (int i = 0; i < 3; ++i) msg.add_mechanism_time_sec(rand_int(0, 300));
    buf.resize(msg.ByteSizeLong());
    msg.SerializeToArray(buf.data(), buf.size());
  } else if (type == "Event") {
    Event msg;
    msg.set_event_id(rand_int(0, 1000));
    msg.set_param(rand_str<10>());
    buf.resize(msg.ByteSizeLong());
    msg.SerializeToArray(buf.data(), buf.size());
  } else if (type == "RobotInjuryStat") {
    RobotInjuryStat msg;
    msg.set_total_damage(rand_uint(0, 10000));
    msg.set_collision_damage(rand_uint(0, 1000));
    msg.set_small_projectile_damage(rand_uint(0, 1000));
    msg.set_large_projectile_damage(rand_uint(0, 1000));
    msg.set_dart_splash_damage(rand_uint(0, 1000));
    msg.set_module_offline_damage(rand_uint(0, 1000));
    msg.set_offline_damage(rand_uint(0, 1000));
    msg.set_penalty_damage(rand_uint(0, 1000));
    msg.set_server_kill_damage(rand_uint(0, 1000));
    msg.set_killer_id(rand_uint(0, 100));
    buf.resize(msg.ByteSizeLong());
    msg.SerializeToArray(buf.data(), buf.size());
  } else if (type == "RobotRespawnStatus") {
    RobotRespawnStatus msg;
    msg.set_is_pending_respawn(rand_bool());
    msg.set_total_respawn_progress(rand_uint(0, 100));
    msg.set_current_respawn_progress(rand_uint(0, 100));
    msg.set_can_free_respawn(rand_bool());
    msg.set_gold_cost_for_respawn(rand_uint(0, 1000));
    msg.set_can_pay_for_respawn(rand_bool());
    buf.resize(msg.ByteSizeLong());
    msg.SerializeToArray(buf.data(), buf.size());
  } else if (type == "RobotStaticStatus") {
    RobotStaticStatus msg;
    msg.set_connection_state(rand_uint(0, 2));
    msg.set_field_state(rand_uint(0, 2));
    msg.set_alive_state(rand_uint(0, 2));
    msg.set_robot_id(rand_uint(0, 100));
    msg.set_robot_type(rand_uint(0, 10));
    msg.set_performance_system_shooter(rand_uint(0, 5));
    msg.set_performance_system_chassis(rand_uint(0, 5));
    msg.set_level(rand_uint(0, 10));
    msg.set_max_health(rand_uint(0, 10000));
    msg.set_max_heat(rand_uint(0, 1000));
    msg.set_heat_cooldown_rate(rand_float(0.1, 10.0));
    msg.set_max_power(rand_uint(0, 1000));
    msg.set_max_buffer_energy(rand_uint(0, 1000));
    msg.set_max_chassis_energy(rand_uint(0, 1000));
    buf.resize(msg.ByteSizeLong());
    msg.SerializeToArray(buf.data(), buf.size());
  } else if (type == "RobotDynamicStatus") {
    RobotDynamicStatus msg;
    msg.set_current_health(rand_uint(0, 10000));
    msg.set_current_heat(rand_float(0, 1000));
    msg.set_last_projectile_fire_rate(rand_float(0, 100));
    msg.set_current_chassis_energy(rand_uint(0, 1000));
    msg.set_current_buffer_energy(rand_uint(0, 1000));
    msg.set_current_experience(rand_uint(0, 10000));
    msg.set_experience_for_upgrade(rand_uint(0, 10000));
    msg.set_total_projectiles_fired(rand_uint(0, 10000));
    msg.set_remaining_ammo(rand_uint(0, 1000));
    msg.set_is_out_of_combat(rand_bool());
    msg.set_out_of_combat_countdown(rand_uint(0, 100));
    msg.set_can_remote_heal(rand_bool());
    msg.set_can_remote_ammo(rand_bool());
    buf.resize(msg.ByteSizeLong());
    msg.SerializeToArray(buf.data(), buf.size());
  } else if (type == "RobotModuleStatus") {
    RobotModuleStatus msg;
    msg.set_power_manager(rand_uint(0, 1));
    msg.set_rfid(rand_uint(0, 1));
    msg.set_light_strip(rand_uint(0, 1));
    msg.set_small_shooter(rand_uint(0, 1));
    msg.set_big_shooter(rand_uint(0, 1));
    msg.set_uwb(rand_uint(0, 1));
    msg.set_armor(rand_uint(0, 1));
    msg.set_video_transmission(rand_uint(0, 1));
    msg.set_capacitor(rand_uint(0, 1));
    msg.set_main_controller(rand_uint(0, 1));
    msg.set_laser_detection_module(rand_uint(0, 1));
    buf.resize(msg.ByteSizeLong());
    msg.SerializeToArray(buf.data(), buf.size());
  } else if (type == "RobotPosition") {
    RobotPosition msg;
    msg.set_x(rand_float(-100, 100));
    msg.set_y(rand_float(-100, 100));
    msg.set_z(rand_float(0, 5));
    msg.set_yaw(rand_float(-3.14, 3.14));
    buf.resize(msg.ByteSizeLong());
    msg.SerializeToArray(buf.data(), buf.size());
  } else if (type == "Buff") {
    Buff msg;
    msg.set_robot_id(rand_uint(0, 100));
    msg.set_buff_type(rand_uint(0, 10));
    msg.set_buff_level(rand_int(0, 5));
    msg.set_buff_max_time(rand_uint(0, 300));
    msg.set_buff_left_time(rand_uint(0, 300));
    buf.resize(msg.ByteSizeLong());
    msg.SerializeToArray(buf.data(), buf.size());
  } else if (type == "PenaltyInfo") {
    PenaltyInfo msg;
    msg.set_penalty_type(rand_uint(0, 10));
    msg.set_penalty_effect_sec(rand_uint(0, 300));
    msg.set_total_penalty_num(rand_uint(0, 10));
    buf.resize(msg.ByteSizeLong());
    msg.SerializeToArray(buf.data(), buf.size());
  } else if (type == "RobotPathPlanInfo") {
    RobotPathPlanInfo msg;
    msg.set_intention(rand_uint(0, 10));
    msg.set_start_pos_x(rand_uint(0, 1000));
    msg.set_start_pos_y(rand_uint(0, 1000));
    for (int i = 0; i < 3; ++i) msg.add_offset_x(rand_int(-100, 100));
    for (int i = 0; i < 3; ++i) msg.add_offset_y(rand_int(-100, 100));
    msg.set_sender_id(rand_uint(0, 100));
    buf.resize(msg.ByteSizeLong());
    msg.SerializeToArray(buf.data(), buf.size());
  } else if (type == "MapClickInfoNotify") {
    MapClickInfoNotify msg;
    msg.set_is_send_all(rand_uint(0, 1));
    msg.set_robot_id(rand_bytes<4>());
    msg.set_mode(rand_uint(0, 5));
    msg.set_enemy_id(rand_uint(0, 100));
    msg.set_ascii(rand_uint(0, 127));
    msg.set_type(rand_uint(0, 10));
    msg.set_screen_x(rand_uint(0, 1920));
    msg.set_screen_y(rand_uint(0, 1080));
    msg.set_map_x(rand_float(-100, 100));
    msg.set_map_y(rand_float(-100, 100));
    buf.resize(msg.ByteSizeLong());
    msg.SerializeToArray(buf.data(), buf.size());
  } else if (type == "RadarInfoToClient") {
    RadarInfoToClient msg;
    msg.set_target_robot_id(rand_uint(0, 100));
    msg.set_target_pos_x(rand_float(-100, 100));
    msg.set_target_pos_y(rand_float(-100, 100));
    msg.set_torward_angle(rand_float(-3.14, 3.14));
    msg.set_is_high_light(rand_uint(0, 1));
    buf.resize(msg.ByteSizeLong());
    msg.SerializeToArray(buf.data(), buf.size());
  } else if (type == "CustomByteBlock") {
    CustomByteBlock msg;
    msg.set_data(rand_bytes<16>());
    buf.resize(msg.ByteSizeLong());
    msg.SerializeToArray(buf.data(), buf.size());
  } else if (type == "AssemblyCommand") {
    AssemblyCommand msg;
    msg.set_operation(rand_uint(0, 10));
    msg.set_difficulty(rand_uint(0, 5));
    buf.resize(msg.ByteSizeLong());
    msg.SerializeToArray(buf.data(), buf.size());
  } else if (type == "TechCoreMotionStateSync") {
    TechCoreMotionStateSync msg;
    msg.set_maximum_difficulty_level(rand_uint(0, 5));
    msg.set_status(rand_uint(0, 3));
    msg.set_enemy_core_status(rand_uint(0, 3));
    msg.set_remain_time_all(rand_uint(0, 300));
    msg.set_remain_time_step(rand_uint(0, 60));
    buf.resize(msg.ByteSizeLong());
    msg.SerializeToArray(buf.data(), buf.size());
  } else if (type == "RobotPerformanceSelectionCommand") {
    RobotPerformanceSelectionCommand msg;
    msg.set_shooter(rand_uint(0, 5));
    msg.set_chassis(rand_uint(0, 5));
    msg.set_sentry_control(rand_uint(0, 3));
    buf.resize(msg.ByteSizeLong());
    msg.SerializeToArray(buf.data(), buf.size());
  } else if (type == "RobotPerformanceSelectionSync") {
    RobotPerformanceSelectionSync msg;
    msg.set_shooter(rand_uint(0, 5));
    msg.set_chassis(rand_uint(0, 5));
    msg.set_sentry_control(rand_uint(0, 3));
    buf.resize(msg.ByteSizeLong());
    msg.SerializeToArray(buf.data(), buf.size());
  } else if (type == "HeroDeployModeEventCommand") {
    HeroDeployModeEventCommand msg;
    msg.set_mode(rand_uint(0, 5));
    buf.resize(msg.ByteSizeLong());
    msg.SerializeToArray(buf.data(), buf.size());
  } else if (type == "DeployModeStatusSync") {
    DeployModeStatusSync msg;
    msg.set_status(rand_uint(0, 3));
    buf.resize(msg.ByteSizeLong());
    msg.SerializeToArray(buf.data(), buf.size());
  } else if (type == "RuneActivateCommand") {
    RuneActivateCommand msg;
    msg.set_activate(rand_uint(0, 1));
    buf.resize(msg.ByteSizeLong());
    msg.SerializeToArray(buf.data(), buf.size());
  } else if (type == "RuneStatusSync") {
    RuneStatusSync msg;
    msg.set_rune_status(rand_uint(0, 3));
    msg.set_activated_arms(rand_uint(0, 10));
    msg.set_average_rings(rand_uint(0, 10));
    buf.resize(msg.ByteSizeLong());
    msg.SerializeToArray(buf.data(), buf.size());
  } else if (type == "SentryStatusSync") {
    SentryStatusSync msg;
    msg.set_posture_id(rand_uint(0, 10));
    msg.set_is_weakened(rand_bool());
    buf.resize(msg.ByteSizeLong());
    msg.SerializeToArray(buf.data(), buf.size());
  } else if (type == "DartCommand") {
    DartCommand msg;
    msg.set_target_id(rand_uint(0, 100));
    msg.set_open(rand_bool());
    msg.set_launch_confirm(rand_bool());
    buf.resize(msg.ByteSizeLong());
    msg.SerializeToArray(buf.data(), buf.size());
  } else if (type == "DartSelectTargetStatusSync") {
    DartSelectTargetStatusSync msg;
    msg.set_target_id(rand_uint(0, 100));
    msg.set_open(rand_uint(0, 2));
    buf.resize(msg.ByteSizeLong());
    msg.SerializeToArray(buf.data(), buf.size());
  } else if (type == "SentryCtrlCommand") {
    SentryCtrlCommand msg;
    msg.set_command_id(rand_uint(0, 100));
    buf.resize(msg.ByteSizeLong());
    msg.SerializeToArray(buf.data(), buf.size());
  } else if (type == "SentryCtrlResult") {
    SentryCtrlResult msg;
    msg.set_command_id(rand_uint(0, 100));
    msg.set_result_code(rand_uint(0, 10));
    buf.resize(msg.ByteSizeLong());
    msg.SerializeToArray(buf.data(), buf.size());
  } else if (type == "AirSupportCommand") {
    AirSupportCommand msg;
    msg.set_command_id(rand_uint(0, 100));
    buf.resize(msg.ByteSizeLong());
    msg.SerializeToArray(buf.data(), buf.size());
  } else if (type == "AirSupportStatusSync") {
    AirSupportStatusSync msg;
    msg.set_airsupport_status(rand_uint(0, 10));
    msg.set_left_time(rand_uint(0, 300));
    msg.set_cost_coins(rand_uint(0, 1000));
    msg.set_is_being_targeted(rand_uint(0, 1));
    msg.set_shooter_status(rand_uint(0, 3));
    buf.resize(msg.ByteSizeLong());
    msg.SerializeToArray(buf.data(), buf.size());
  } else if (type == "CommonCommand") {
    CommonCommand msg;
    msg.set_cmd_type(rand_uint(0, 10));
    msg.set_param(rand_uint(0, 100));
    buf.resize(msg.ByteSizeLong());
    msg.SerializeToArray(buf.data(), buf.size());
  }
  return {type, buf};
}

// 兼容老接口：随机类型
std::pair<std::string, std::vector<uint8_t>> build_random_message() {
  static const std::vector<std::string> types = {
      "KeyboardMouseControl",
      "CustomControl",
      "GameStatus",
      "GlobalUnitStatus",
      "GlobalLogisticsStatus",
      "GlobalSpecialMechanism",
      "Event",
      "RobotInjuryStat",
      "RobotRespawnStatus",
      "RobotStaticStatus",
      "RobotDynamicStatus",
      "RobotModuleStatus",
      "RobotPosition",
      "Buff",
      "PenaltyInfo",
      "RobotPathPlanInfo",
      "MapClickInfoNotify",
      "RadarInfoToClient",
      "CustomByteBlock",
      "AssemblyCommand",
      "TechCoreMotionStateSync",
      "RobotPerformanceSelectionCommand",
      "RobotPerformanceSelectionSync",
      "CommonCommand",
      "HeroDeployModeEventCommand",
      "DeployModeStatusSync",
      "RuneActivateCommand",
      "RuneStatusSync",
      "SentryStatusSync",
      "DartCommand",
      "DartSelectTargetStatusSync",
      "SentryCtrlCommand",
      "SentryCtrlResult",
      "AirSupportCommand",
      "AirSupportStatusSync"};
  int idx = rand() % types.size();
  return build_random_message(types[idx]);
}

// 打印消息内容工具
void print_message_from_topic_payload(const std::string& topic,
                                      const std::vector<uint8_t>& payload) {
  // 调试：无论能否解析，先打印到达的topic与payload长度，便于端到端排查
  std::cout << "[Debug] 收到topic=" << topic
            << ", payloadLen=" << payload.size() << std::endl;
  // 简单示例：根据topic或payload长度猜测类型，实际可根据topic或自定义头区分
  // 这里只做全部尝试反序列化，成功就打印
  bool printed = false;
  do {
    KeyboardMouseControl msg;
    if (msg.ParseFromArray(payload.data(), payload.size())) {
      std::cout << "[Recv] KeyboardMouseControl: mouse_x=" << msg.mouse_x()
                << ", mouse_y=" << msg.mouse_y() << std::endl;
      printed = true;
      break;
    }
  } while (0);
  do {
    GameStatus msg;
    if (msg.ParseFromArray(payload.data(), payload.size())) {
      std::cout << "[Recv] GameStatus: round=" << msg.current_round()
                << ", red_score=" << msg.red_score() << std::endl;
      printed = true;
      break;
    }
  } while (0);
  do {
    GlobalUnitStatus msg;
    if (msg.ParseFromArray(payload.data(), payload.size())) {
      std::cout << "[Recv] GlobalUnitStatus: base_health=" << msg.base_health()
                << ", outpost_health=" << msg.outpost_health() << std::endl;
      printed = true;
      break;
    }
  } while (0);
  // ...可继续添加所有33种消息类型的打印，或用宏/模板优化...
  if (!printed) {
    std::cout << "[Recv] Unknown or unrecognized message on topic: " << topic
              << ", payload size: " << payload.size() << std::endl;
  }
}
