
#include "worker.h"

Worker::Worker(QObject* parent) : QObject(parent) {}

#include <QRandomGenerator>
#include <QStringList>

// 与 mainwindow.cpp 保持一致，定义消息类型列表
static QStringList serverToClientTypes = {"GameStatus",
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
                                          "RaderInfoToClient",
                                          "CustomByteBlock",
                                          "TechCoreMotionStateSync",
                                          "RobotPerformanceSelectionSync",
                                          "DeployModeStatusSync",
                                          "RuneStatusSync",
                                          "SentinelStatusSync",
                                          "DartSelectTargetStatusSync",
                                          "GuardCtrlResult",
                                          "AirSupportStatusSync"};

void Worker::startWork() {
  // 定时器模拟“服务器->客户端”消息
  QTimer* timer = new QTimer(this);
  connect(timer, &QTimer::timeout, this, [this]() {
    for (const auto& type : serverToClientTypes) {
      QVariantMap sendMsg;
      QString logValue;
      // 补全所有协议类型的模拟数据
      if (type == "GameStatus") {
        sendMsg["current_round"] = QRandomGenerator::global()->bounded(1, 10);
        sendMsg["total_rounds"] = 10;
        sendMsg["red_score"] = QRandomGenerator::global()->bounded(0, 100);
        sendMsg["blue_score"] = QRandomGenerator::global()->bounded(0, 100);
        sendMsg["current_stage"] = QRandomGenerator::global()->bounded(1, 5);
        sendMsg["stage_countdown_sec"] =
            QRandomGenerator::global()->bounded(0, 300);
        sendMsg["stage_elapsed_sec"] =
            QRandomGenerator::global()->bounded(0, 300);
        sendMsg["is_paused"] = QRandomGenerator::global()->bounded(0, 2);
      } else if (type == "GlobalUnitStatus") {
        sendMsg["base_health"] = QRandomGenerator::global()->bounded(0, 10000);
        sendMsg["base_status"] = QRandomGenerator::global()->bounded(0, 2);
        sendMsg["base_shield"] = QRandomGenerator::global()->bounded(0, 1000);
        sendMsg["outpost_health"] =
            QRandomGenerator::global()->bounded(0, 10000);
        sendMsg["outpost_status"] = QRandomGenerator::global()->bounded(0, 2);
        sendMsg["robot_health"] = QRandomGenerator::global()->bounded(0, 10000);
        sendMsg["robot_bullets"] = QRandomGenerator::global()->bounded(0, 500);
        sendMsg["total_damage_red"] =
            QRandomGenerator::global()->bounded(0, 10000);
        sendMsg["total_damage_blue"] =
            QRandomGenerator::global()->bounded(0, 10000);
      } else if (type == "GlobalLogisticsStatus") {
        sendMsg["remaining_economy"] =
            QRandomGenerator::global()->bounded(0, 10000);
        sendMsg["total_economy_obtained"] =
            QRandomGenerator::global()->bounded(0, 1000000);
        sendMsg["tech_level"] = QRandomGenerator::global()->bounded(1, 10);
        sendMsg["encryption_level"] =
            QRandomGenerator::global()->bounded(1, 10);
      } else if (type == "GlobalSpecialMechanism") {
        sendMsg["mechanism_id"] = QRandomGenerator::global()->bounded(1, 10);
        sendMsg["mechanism_time_sec"] =
            QRandomGenerator::global()->bounded(0, 100);
      } else if (type == "Event") {
        sendMsg["event_id"] = QRandomGenerator::global()->bounded(1, 100);
        sendMsg["param"] = QString("test");
      } else if (type == "RobotInjuryStat") {
        sendMsg["total_damage"] = QRandomGenerator::global()->bounded(0, 10000);
        sendMsg["collision_damage"] =
            QRandomGenerator::global()->bounded(0, 1000);
        sendMsg["small_projectile_damage"] =
            QRandomGenerator::global()->bounded(0, 1000);
        sendMsg["large_projectile_damage"] =
            QRandomGenerator::global()->bounded(0, 1000);
        sendMsg["dart_splash_damage"] =
            QRandomGenerator::global()->bounded(0, 1000);
        sendMsg["module_offline_damage"] =
            QRandomGenerator::global()->bounded(0, 1000);
        sendMsg["wifi_offline_damage"] =
            QRandomGenerator::global()->bounded(0, 1000);
        sendMsg["penalty_damage"] =
            QRandomGenerator::global()->bounded(0, 1000);
        sendMsg["server_kill_damage"] =
            QRandomGenerator::global()->bounded(0, 1000);
        sendMsg["killer_id"] = QRandomGenerator::global()->bounded(1, 10);
      } else if (type == "RobotRespawnStatus") {
        sendMsg["is_pending_respawn"] =
            QRandomGenerator::global()->bounded(0, 2);
        sendMsg["total_respawn_progress"] =
            QRandomGenerator::global()->bounded(0, 100);
        sendMsg["current_respawn_progress"] =
            QRandomGenerator::global()->bounded(0, 100);
        sendMsg["can_free_respawn"] = QRandomGenerator::global()->bounded(0, 2);
        sendMsg["gold_cost_for_respawn"] =
            QRandomGenerator::global()->bounded(0, 1000);
        sendMsg["can_pay_for_respawn"] =
            QRandomGenerator::global()->bounded(0, 2);
      } else if (type == "RobotStaticStatus") {
        sendMsg["connection_state"] = QRandomGenerator::global()->bounded(0, 2);
        sendMsg["field_state"] = QRandomGenerator::global()->bounded(0, 2);
        sendMsg["alive_state"] = QRandomGenerator::global()->bounded(0, 2);
        sendMsg["robot_id"] = QRandomGenerator::global()->bounded(1, 10);
        sendMsg["robot_type"] = QRandomGenerator::global()->bounded(1, 5);
        sendMsg["performance_system_shooter"] =
            QRandomGenerator::global()->bounded(0, 2);
        sendMsg["performance_system_chassis"] =
            QRandomGenerator::global()->bounded(0, 2);
        sendMsg["level"] = QRandomGenerator::global()->bounded(1, 10);
        sendMsg["max_health"] =
            QRandomGenerator::global()->bounded(1000, 10000);
        sendMsg["max_heat"] = QRandomGenerator::global()->bounded(100, 1000);
        sendMsg["heat_cooldown_rate"] =
            QRandomGenerator::global()->bounded(1, 100);
        sendMsg["max_power"] = QRandomGenerator::global()->bounded(100, 1000);
        sendMsg["max_buffer_energy"] =
            QRandomGenerator::global()->bounded(100, 1000);
        sendMsg["max_chassis_energy"] =
            QRandomGenerator::global()->bounded(100, 1000);
      } else if (type == "RobotDynamicStatus") {
        sendMsg["current_health"] =
            QRandomGenerator::global()->bounded(0, 10000);
        sendMsg["current_heat"] =
            QRandomGenerator::global()->bounded(0, 1000) / 10.0;
        sendMsg["last_projectile_fire_rate"] =
            QRandomGenerator::global()->bounded(0, 100) / 10.0;
        sendMsg["current_chassis_energy"] =
            QRandomGenerator::global()->bounded(0, 1000);
        sendMsg["current_buffer_energy"] =
            QRandomGenerator::global()->bounded(0, 1000);
        sendMsg["current_experience"] =
            QRandomGenerator::global()->bounded(0, 1000);
        sendMsg["experience_for_upgrade"] =
            QRandomGenerator::global()->bounded(0, 1000);
        sendMsg["total_projectiles_fired"] =
            QRandomGenerator::global()->bounded(0, 1000);
        sendMsg["remaining_ammo"] =
            QRandomGenerator::global()->bounded(0, 1000);
        sendMsg["is_out_of_combat"] = QRandomGenerator::global()->bounded(0, 2);
        sendMsg["out_of_combat_countdown"] =
            QRandomGenerator::global()->bounded(0, 100);
        sendMsg["can_remote_heal"] = QRandomGenerator::global()->bounded(0, 2);
        sendMsg["can_remote_ammo"] = QRandomGenerator::global()->bounded(0, 2);
      } else if (type == "RobotModuleStatus") {
        sendMsg["power_manager"] = QRandomGenerator::global()->bounded(0, 2);
        sendMsg["rfid"] = QRandomGenerator::global()->bounded(0, 2);
        sendMsg["light_strip"] = QRandomGenerator::global()->bounded(0, 2);
        sendMsg["small_shooter"] = QRandomGenerator::global()->bounded(0, 2);
        sendMsg["big_shooter"] = QRandomGenerator::global()->bounded(0, 2);
        sendMsg["uwb"] = QRandomGenerator::global()->bounded(0, 2);
        sendMsg["armor"] = QRandomGenerator::global()->bounded(0, 2);
        sendMsg["video_transmission"] =
            QRandomGenerator::global()->bounded(0, 2);
        sendMsg["capacitor"] = QRandomGenerator::global()->bounded(0, 2);
        sendMsg["main_controller"] = QRandomGenerator::global()->bounded(0, 2);
      } else if (type == "RobotPosition") {
        sendMsg["x"] = QRandomGenerator::global()->bounded(-100, 100);
        sendMsg["y"] = QRandomGenerator::global()->bounded(-100, 100);
        sendMsg["z"] = QRandomGenerator::global()->bounded(0, 100);
        sendMsg["yaw"] = QRandomGenerator::global()->bounded(0, 360);
      } else if (type == "Buff") {
        sendMsg["robot_id"] = QRandomGenerator::global()->bounded(1, 10);
        sendMsg["buff_type"] = QRandomGenerator::global()->bounded(1, 8);
        sendMsg["buff_level"] = QRandomGenerator::global()->bounded(1, 5);
        sendMsg["buff_max_time"] = QRandomGenerator::global()->bounded(10, 100);
        sendMsg["buff_left_time"] = QRandomGenerator::global()->bounded(0, 100);
        sendMsg["msg_params"] = QString("test");
      } else if (type == "PenaltyInfo") {
        sendMsg["penalty_type"] = QRandomGenerator::global()->bounded(1, 7);
        sendMsg["penalty_effect_sec"] =
            QRandomGenerator::global()->bounded(0, 60);
        sendMsg["total_penalty_num"] =
            QRandomGenerator::global()->bounded(0, 10);
      } else if (type == "RobotPathPlanInfo") {
        sendMsg["intention"] = QRandomGenerator::global()->bounded(0, 10);
        sendMsg["start_pos_x"] = QRandomGenerator::global()->bounded(0, 100);
        sendMsg["start_pos_y"] = QRandomGenerator::global()->bounded(0, 100);
        sendMsg["offset_x"] = QRandomGenerator::global()->bounded(0, 100);
        sendMsg["offset_y"] = QRandomGenerator::global()->bounded(0, 100);
        sendMsg["sender_id"] = QRandomGenerator::global()->bounded(1, 10);
      } else if (type == "MapClickInfoNotify") {
        sendMsg["is_send_all"] = QRandomGenerator::global()->bounded(0, 2);
        sendMsg["robot_id"] = QRandomGenerator::global()->bounded(1, 10);
        sendMsg["mode"] = QRandomGenerator::global()->bounded(0, 5);
        sendMsg["enemy_id"] = QRandomGenerator::global()->bounded(1, 10);
        sendMsg["ascii"] = QRandomGenerator::global()->bounded(0, 128);
        sendMsg["type"] = QRandomGenerator::global()->bounded(0, 10);
        sendMsg["screen_x"] = QRandomGenerator::global()->bounded(0, 1920);
        sendMsg["screen_y"] = QRandomGenerator::global()->bounded(0, 1080);
        sendMsg["map_x"] = QRandomGenerator::global()->bounded(0, 100);
        sendMsg["map_y"] = QRandomGenerator::global()->bounded(0, 100);
      } else if (type == "RaderInfoToClient") {
        sendMsg["target_robot_id"] = QRandomGenerator::global()->bounded(1, 10);
        sendMsg["target_pos_x"] = QRandomGenerator::global()->bounded(0, 100);
        sendMsg["target_pos_y"] = QRandomGenerator::global()->bounded(0, 100);
        sendMsg["torward_angle"] = QRandomGenerator::global()->bounded(0, 360);
        sendMsg["is_high_light"] = QRandomGenerator::global()->bounded(0, 2);
      } else if (type == "CustomByteBlock") {
        sendMsg["data"] = QString("deadbeef");
      } else if (type == "TechCoreMotionStateSync") {
        sendMsg["maximum_difficulty_level"] =
            QRandomGenerator::global()->bounded(1, 10);
        sendMsg["status"] = QRandomGenerator::global()->bounded(0, 5);
      } else if (type == "RobotPerformanceSelectionSync") {
        sendMsg["shooter"] = QRandomGenerator::global()->bounded(0, 2);
        sendMsg["chassis"] = QRandomGenerator::global()->bounded(0, 2);
      } else if (type == "DeployModeStatusSync") {
        sendMsg["status"] = QRandomGenerator::global()->bounded(0, 5);
      } else if (type == "RuneStatusSync") {
        sendMsg["rune_status"] = QRandomGenerator::global()->bounded(0, 2);
        sendMsg["activated_arms"] = QRandomGenerator::global()->bounded(0, 10);
        sendMsg["average_rings"] = QRandomGenerator::global()->bounded(0, 100);
      } else if (type == "SentinelStatusSync") {
        sendMsg["posture_id"] = QRandomGenerator::global()->bounded(0, 10);
        sendMsg["is_weakened"] = QRandomGenerator::global()->bounded(0, 2);
      } else if (type == "DartSelectTargetStatusSync") {
        sendMsg["target_id"] = QRandomGenerator::global()->bounded(1, 10);
        sendMsg["open"] = QRandomGenerator::global()->bounded(0, 2);
      } else if (type == "GuardCtrlResult") {
        sendMsg["command_id"] = QRandomGenerator::global()->bounded(1, 10);
        sendMsg["result_code"] = QRandomGenerator::global()->bounded(0, 5);
      } else if (type == "AirSupportStatusSync") {
        sendMsg["airsupport_status"] =
            QRandomGenerator::global()->bounded(0, 5);
        sendMsg["left_time"] = QRandomGenerator::global()->bounded(0, 100);
        sendMsg["cost_coins"] = QRandomGenerator::global()->bounded(0, 1000);
      }
      // 自动拼接所有字段为key:value; key:value; ...
      QStringList logPairs;
      for (auto it = sendMsg.constBegin(); it != sendMsg.constEnd(); ++it) {
        logPairs << it.key() + ":" + QVariant(it.value()).toString();
      }
      logValue = logPairs.join(" ; ");
      emit messageUpdated(type, sendMsg, true);
      emit logUpdated(
          QString("[WORKER] 服务器->客户端: %1, 值: %2").arg(type, logValue));
      emit statsUpdated(type, QRandomGenerator::global()->bounded(100), 0);
    }
  });
  timer->start(1000);  // 每1000ms生成一次

  // 定时器模拟“客户端->服务器”类型的接收数据
  static QStringList clientToServerTypes = {"AssemblyCommand",
                                            "RobotPerformanceSelectionCommand",
                                            "HeroDeployModeEventCommand",
                                            "RuneActivateCommand",
                                            "DartCommand",
                                            "GuardCtrlCommand",
                                            "AirSupportCommand"};
  QTimer* recvTimer = new QTimer(this);
  connect(recvTimer, &QTimer::timeout, this, [this]() {
    for (const auto& type : clientToServerTypes) {
      QVariantMap recvMsg;
      QString logValue;
      // 按协议字段补全模拟数据
      if (type == "AssemblyCommand") {
        recvMsg["operation"] = QRandomGenerator::global()->bounded(0, 10);
        recvMsg["difficulty"] = QRandomGenerator::global()->bounded(0, 5);
      } else if (type == "RobotPerformanceSelectionCommand") {
        recvMsg["shooter"] = QRandomGenerator::global()->bounded(0, 5);
        recvMsg["chassis"] = QRandomGenerator::global()->bounded(0, 5);
      } else if (type == "HeroDeployModeEventCommand") {
        recvMsg["mode"] = QRandomGenerator::global()->bounded(0, 5);
      } else if (type == "RuneActivateCommand") {
        recvMsg["activate"] = QRandomGenerator::global()->bounded(0, 2);
      } else if (type == "DartCommand") {
        recvMsg["target_id"] = QRandomGenerator::global()->bounded(1, 10);
        recvMsg["open"] = QRandomGenerator::global()->bounded(0, 2);
      } else if (type == "GuardCtrlCommand") {
        recvMsg["command_id"] = QRandomGenerator::global()->bounded(1, 10);
      } else if (type == "AirSupportCommand") {
        recvMsg["command_id"] = QRandomGenerator::global()->bounded(1, 10);
      }
      // 自动拼接所有字段为key:value; key:value; ...
      QStringList logPairs;
      for (auto it = recvMsg.constBegin(); it != recvMsg.constEnd(); ++it) {
        logPairs << it.key() + ":" + QVariant(it.value()).toString();
      }
      logValue = logPairs.join(" ; ");
      emit messageUpdated(type, recvMsg, false);
      emit logUpdated(
          QString("[WORKER] 客户端->服务器: %1, 值: %2").arg(type, logValue));
      emit statsUpdated(type, 0, QRandomGenerator::global()->bounded(100));
    }
  });
  recvTimer->start(1200);  // 每1200ms生成一次
}
