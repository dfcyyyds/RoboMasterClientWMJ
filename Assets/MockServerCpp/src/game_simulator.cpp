#include "game_simulator.h"

#include <algorithm>
#include <cmath>

namespace {
// 简单工具
constexpr float kEpsilon = 1e-4f;

template <typename T>
T clampValue(T v, T min_v, T max_v) {
  return std::max(min_v, std::min(max_v, v));
}

}  // namespace

// ═══════════════════════════════════════════════════════════════
// GameSimulator 实现
// ═══════════════════════════════════════════════════════════════

GameSimulator::GameSimulator() : rng_(std::random_device{}()) {}

float GameSimulator::randFloat(float min, float max) {
  std::uniform_real_distribution<float> dist(min, max);
  return dist(rng_);
}

int GameSimulator::randInt(int min, int max) {
  std::uniform_int_distribution<int> dist(min, max);
  return dist(rng_);
}

bool GameSimulator::randChance(float probability) {
  if (probability <= 0.0f) return false;
  if (probability >= 1.0f) return true;
  std::uniform_real_distribution<float> dist(0.0f, 1.0f);
  return dist(rng_) < probability;
}

void GameSimulator::init(int self_robot_index, bool fast_mode) {
  self_robot_idx_ = clampValue(self_robot_index, 0, NUM_ROBOTS - 1);
  fast_mode_ = fast_mode;
  buff_send_index_ = 0;

  stage_ = GameStage::Preparation;
  stage_timer_ = 0.0f;
  match_elapsed_ = 0.0f;
  total_elapsed_ = 0.0f;

  current_round_ = 1;
  total_rounds_ = 3;
  red_score_ = 0;
  blue_score_ = 0;

  ally_ = TeamState();
  enemy_ = TeamState();

  // 初始化队伍：默认己方在左（x 小），敌方在右（x 大）
  const float ally_x = 4.0f;
  const float enemy_x = ARENA_WIDTH - 4.0f;
  const float dy = ARENA_HEIGHT / (NUM_ROBOTS + 1);

  initRobot(ally_.robots[0], RobotType::Hero, 1, ally_x, dy * 1);
  initRobot(ally_.robots[1], RobotType::Engineer, 2, ally_x, dy * 2);
  initRobot(ally_.robots[2], RobotType::Infantry3, 3, ally_x, dy * 3);
  initRobot(ally_.robots[3], RobotType::Infantry4, 4, ally_x, dy * 4);
  initRobot(ally_.robots[4], RobotType::Sentry, 7, ally_x, dy * 5);

  initRobot(enemy_.robots[0], RobotType::Hero, 101, enemy_x, dy * 1);
  initRobot(enemy_.robots[1], RobotType::Engineer, 102, enemy_x, dy * 2);
  initRobot(enemy_.robots[2], RobotType::Infantry3, 103, enemy_x, dy * 3);
  initRobot(enemy_.robots[3], RobotType::Infantry4, 104, enemy_x, dy * 4);
  initRobot(enemy_.robots[4], RobotType::Sentry, 107, enemy_x, dy * 5);

  ally_.remaining_economy = INITIAL_GOLD;
  ally_.total_economy_obtained = INITIAL_GOLD;
  enemy_.remaining_economy = INITIAL_GOLD;
  enemy_.total_economy_obtained = INITIAL_GOLD;

  ally_.base_health = BASE_MAX_HEALTH;
  enemy_.base_health = BASE_MAX_HEALTH;
  ally_.base_status = UnitStatus::Invincible;
  enemy_.base_status = UnitStatus::Invincible;

  ally_.outpost_health = OUTPOST_MAX_HEALTH;
  enemy_.outpost_health = OUTPOST_MAX_HEALTH;
  ally_.outpost_status = UnitStatus::Normal;
  enemy_.outpost_status = UnitStatus::Normal;

  economy_10s_timer_ = 0.0f;
  gold_tick_index_ = 0;
  ammo_exchange_timer_ = 0.0f;

  pending_events_.clear();
  first_blood_happened_ = false;
}

void GameSimulator::tick(float dt) {
  if (dt <= 0.0f) return;

  total_elapsed_ += dt;
  stage_timer_ += dt;
  if (stage_ == GameStage::InProgress) {
    match_elapsed_ += dt;
  }

  tickStageTransition();

  // 比赛未开始或已结束，仅做阶段推进
  if (stage_ != GameStage::InProgress) return;

  tickEconomy(dt);
  tickAmmoExchange(dt);
  tickCombat(dt);
  tickOutpostBaseDamage(dt);
  tickHeatCooldown(dt);
  tickRespawn(dt);
  tickBuffs(dt);
  tickOutOfCombat(dt);
  tickSupplyZone(dt);
  tickRemoteHeal(dt);
  tickPositions(dt);
  tickExperience(dt);
  tickAirSupport(dt);
  tickRune(dt);
  tickOutpost(dt);
  tickSentryPosture(dt);
  tickSentrySupply(dt);
  tickEngineerDefense(dt);
  tickAssembly(dt);
  tickFortress(dt);
  tickRadarMark(dt);
  tickDart(dt);
  tickChassisEnergy(dt);
  tickBuffEffects();
}

void GameSimulator::initRobot(RobotState& r, RobotType type, uint32_t id,
                              float sx, float sy) {
  r = RobotState();
  r.type = type;
  r.robot_id = id;
  r.shooter_type = 1;
  r.chassis_type = 1;
  r.sentry_ctrl = SentryControl::Auto;

  r.is_connected = true;
  r.is_alive = true;
  r.is_on_field = true;

  r.level = 1;
  r.experience = 0;
  r.max_level = 5;

  updatePerformanceStats(r);
  r.current_health = r.max_health;
  r.current_heat = 0.0f;
  r.current_chassis_energy = CHASSIS_ENERGY_INITIAL;
  r.current_buffer_energy = BUFFER_ENERGY_MAX;

  r.remaining_ammo = (type == RobotType::Sentry) ? SENTRY_INITIAL_AMMO : 300;

  r.pos_x = sx;
  r.pos_y = sy;
  r.pos_z = 0.0f;
  r.yaw = 0.0f;
  r.target_x = sx;
  r.target_y = sy;
  r.move_timer = 0.0f;

  r.is_deployed = (type == RobotType::Sentry);
}

uint32_t GameSimulator::getLevelForExp(uint32_t exp, uint32_t max_level) {
  uint32_t level = 1;
  for (int i = 0; i < 10 && level < max_level; ++i) {
    if (exp >= EXP_TABLE[i]) level = i + 1;
  }
  return clampValue(level, 1u, max_level);
}

void GameSimulator::updatePerformanceStats(RobotState& r) {
  uint32_t level = getLevelForExp(r.experience, r.max_level);

  switch (r.type) {
    case RobotType::Hero: {
      const PerformanceStats& s = HERO_STATS[0][level - 1];
      r.max_health = s.max_health;
      r.max_power = s.max_power;
      r.max_heat = s.max_heat;
      r.base_cooldown_rate = s.heat_cooldown_rate;
      r.heat_cooldown_rate = s.heat_cooldown_rate;
      break;
    }
    case RobotType::Infantry3:
    case RobotType::Infantry4: {
      const ChassisStats& c = INFANTRY_CHASSIS[0][level - 1];
      const ShooterStats& s = INFANTRY_SHOOTER[0][level - 1];
      r.max_health = c.max_health;
      r.max_power = c.max_power;
      r.max_heat = s.max_heat;
      r.base_cooldown_rate = s.heat_cooldown_rate;
      r.heat_cooldown_rate = s.heat_cooldown_rate;
      break;
    }
    case RobotType::Engineer: {
      // 工程：稍高生命，适中功率
      r.max_health = ENGINEER_HEALTH;
      r.max_power = ENGINEER_POWER;
      r.max_heat = 120;
      r.base_cooldown_rate = 10.0f;
      r.heat_cooldown_rate = 10.0f;
      break;
    }
    case RobotType::Sentry: {
      r.max_health = SENTRY_AUTO_HEALTH;
      r.max_power = SENTRY_AUTO_POWER;
      r.max_heat = SENTRY_AUTO_HEAT;
      r.base_cooldown_rate = 8.0f;
      r.heat_cooldown_rate = 8.0f;
      break;
    }
  }
}

void GameSimulator::recalcEffectiveStats(RobotState& r, TeamState& team) {
  // 根据队伍能量机关/堡垒等状态计算有效攻防系数
  float atk = 1.0f;
  float def = 0.0f;
  float cooldown_mult = 1.0f;

  // 能量机关增益
  atk += team.rune.attack_bonus;
  def += team.rune.defense_bonus;
  cooldown_mult *= team.rune.cooldown_mult;

  // 堡垒
  if (team.fortress.activated) {
    def += FORTRESS_DEFENSE;
  }

  r.eff_attack = atk;
  r.eff_defense = def;
  r.eff_cooldown_mult = cooldown_mult;
}

void GameSimulator::tickStageTransition() {
  const float prep_dur = fast_mode_ ? FAST_PREP_DURATION : PREP_DURATION;
  const float self_dur =
      fast_mode_ ? FAST_SELFCHECK_DURATION : SELFCHECK_DURATION;
  const float cd_dur =
      fast_mode_ ? FAST_COUNTDOWN_DURATION : COUNTDOWN_DURATION;
  const float match_dur = fast_mode_ ? FAST_MATCH_DURATION : MATCH_DURATION;

  // 任意一方基地被击毁则直接结束
  bool base_dead = (ally_.base_health == 0 || enemy_.base_health == 0);
  if (stage_ == GameStage::InProgress && base_dead) {
    stage_ = GameStage::Ended;
    stage_timer_ = 0.0f;
    std::string result = "draw";
    if (ally_.base_health == 0 && enemy_.base_health > 0) result = "blue_win";
    if (enemy_.base_health == 0 && ally_.base_health > 0) result = "red_win";
    pushEvent(EventId::EXT_MATCH_END, result);
    return;
  }

  switch (stage_) {
    case GameStage::NotStarted:
      stage_ = GameStage::Preparation;
      stage_timer_ = 0.0f;
      pushEvent(EventId::EXT_STAGE_CHANGE, "Preparation");
      break;
    case GameStage::Preparation:
      if (stage_timer_ >= prep_dur) {
        stage_ = GameStage::SelfCheck;
        stage_timer_ = 0.0f;
        pushEvent(EventId::EXT_STAGE_CHANGE, "SelfCheck");
      }
      break;
    case GameStage::SelfCheck:
      if (stage_timer_ >= self_dur) {
        stage_ = GameStage::Countdown;
        stage_timer_ = 0.0f;
        pushEvent(EventId::EXT_STAGE_CHANGE, "Countdown");
      }
      break;
    case GameStage::Countdown:
      if (stage_timer_ >= cd_dur) {
        stage_ = GameStage::InProgress;
        stage_timer_ = 0.0f;
        match_elapsed_ = 0.0f;
        pushEvent(EventId::EXT_MATCH_START, "");
      }
      break;
    case GameStage::InProgress:
      if (match_elapsed_ >= match_dur && !base_dead) {
        stage_ = GameStage::Ended;
        stage_timer_ = 0.0f;
        std::string result = "draw";
        if (ally_.base_health > enemy_.base_health)
          result = "red_win";
        else if (enemy_.base_health > ally_.base_health)
          result = "blue_win";
        pushEvent(EventId::EXT_MATCH_END, result);
      }
      break;
    case GameStage::Ended:
      break;
  }
}

void GameSimulator::tickEconomy(float dt) {
  if (stage_ != GameStage::InProgress) return;

  // 按照 GOLD_ELAPSED_TIMES/GOLD_AMOUNTS 表发放金币
  while (gold_tick_index_ < GOLD_TICK_COUNT &&
         match_elapsed_ >= GOLD_ELAPSED_TIMES[gold_tick_index_]) {
    uint32_t amount = GOLD_AMOUNTS[gold_tick_index_];

    ally_.remaining_economy += amount;
    ally_.total_economy_obtained += amount;
    enemy_.remaining_economy += amount;
    enemy_.total_economy_obtained += amount;

    pushEvent(EventId::EXT_GOLD_INCOME,
              "red:" + std::to_string(amount) + ":periodic");
    pushEvent(EventId::EXT_GOLD_INCOME,
              "blue:" + std::to_string(amount) + ":periodic");

    ++gold_tick_index_;
  }

  economy_10s_timer_ += dt;
}

void GameSimulator::tickAmmoExchange(float dt) {
  if (stage_ != GameStage::InProgress) return;

  ammo_exchange_timer_ += dt;
  if (ammo_exchange_timer_ < 5.0f) return;
  ammo_exchange_timer_ = 0.0f;

  auto exchange_for_team = [&](TeamState& team, const char* team_name) {
    if (team.remaining_economy < AMMO_17MM_COST) return;

    // 找到弹药不足的机器人
    for (int i = 0; i < NUM_ROBOTS; ++i) {
      RobotState& r = team.robots[i];
      if (!r.is_alive) continue;
      if (r.remaining_ammo > 800) continue;

      uint32_t cost = AMMO_17MM_COST;
      if (team.remaining_economy < cost) break;

      team.remaining_economy -= cost;
      team.total_17mm_exchanged += AMMO_17MM_AMOUNT;
      r.remaining_ammo += AMMO_17MM_AMOUNT;

      std::string param = std::to_string(r.robot_id) +
                          ":17mm:" + std::to_string(AMMO_17MM_AMOUNT) + ":" +
                          std::to_string(cost);
      pushEvent(EventId::EXT_AMMO_EXCHANGED, param);
      break;
    }
  };

  exchange_for_team(ally_, "red");
  exchange_for_team(enemy_, "blue");
}

void GameSimulator::tickCombat(float dt) {
  if (stage_ != GameStage::InProgress) return;

  auto simulate_pair = [&](TeamState& atk_team, TeamState& def_team) {
    for (int i = 0; i < NUM_ROBOTS; ++i) {
      RobotState& attacker = atk_team.robots[i];
      if (!attacker.is_alive) continue;
      if (attacker.remaining_ammo <= 0) continue;

      // 简单射击频率：平均每 1.5s 发射一次
      float fire_prob = dt / 1.5f;
      if (!randChance(fire_prob)) continue;

      // 选择一个敌人
      int target_idx = randInt(0, NUM_ROBOTS - 1);
      RobotState& target = def_team.robots[target_idx];
      if (!target.is_alive) continue;

      bool is_17mm = true;
      uint32_t dmg = is_17mm ? DMG_17MM : DMG_42MM;

      // 簇射：连续几发
      int shots = 1 + randInt(0, 2);
      for (int s = 0; s < shots && attacker.remaining_ammo > 0; ++s) {
        attacker.remaining_ammo--;
        attacker.total_projectiles_fired++;
        attacker.current_heat += is_17mm ? HEAT_PER_17MM : HEAT_PER_42MM;
        dealDamage(target, def_team, dmg, attacker.robot_id, is_17mm, false);

        attacker.no_fire_timer = 0.0f;
        target.no_damage_timer = 0.0f;
      }

      attacker.last_fire_speed = shots / dt;
    }
  };

  simulate_pair(ally_, enemy_);
  simulate_pair(enemy_, ally_);
}

void GameSimulator::tickOutpostBaseDamage(float /*dt*/) {
  // 由 dealDamage/killRobot 内部调用 dealBaseDamage/dealOutpostDamage
  // 这里无需额外逻辑
}

void GameSimulator::tickHeatCooldown(float dt) {
  auto cool_team = [&](TeamState& t) {
    for (int i = 0; i < NUM_ROBOTS; ++i) {
      RobotState& r = t.robots[i];
      if (!r.is_alive) continue;

      float rate = r.heat_cooldown_rate * r.eff_cooldown_mult;
      if (t.fortress.activated) {
        rate += t.fortress.heat_cooldown_bonus;
      }

      r.current_heat -= rate * dt;
      if (r.current_heat < 0.0f) r.current_heat = 0.0f;
    }
  };

  cool_team(ally_);
  cool_team(enemy_);
}

void GameSimulator::tickRespawn(float dt) {
  auto process_team = [&](TeamState& team, const char* team_name) {
    for (int i = 0; i < NUM_ROBOTS; ++i) {
      RobotState& r = team.robots[i];
      if (!r.is_pending_respawn) continue;

      r.respawn_progress += dt;
      if (r.respawn_progress >= r.total_respawn_progress) {
        bool instant = false;
        uint32_t cost = calcRespawnGoldCost(r);
        if (team.remaining_economy >= cost && randChance(0.3f)) {
          team.remaining_economy -= cost;
          r.instant_respawn_count++;
          instant = true;
          pushEvent(EventId::EXT_ROBOT_INSTANT_RESPAWN,
                    std::to_string(r.robot_id) + ":" + std::to_string(cost));
        }
        respawnRobot(r, instant);
        pushEvent(EventId::EXT_ROBOT_RESPAWN, std::to_string(r.robot_id));
      }
    }
  };

  process_team(ally_, "red");
  process_team(enemy_, "blue");
}

void GameSimulator::tickBuffs(float dt) {
  auto process_team = [&](TeamState& team) {
    for (int i = 0; i < NUM_ROBOTS; ++i) {
      RobotState& r = team.robots[i];
      for (auto it = r.buffs.begin(); it != r.buffs.end();) {
        it->left_time -= dt;
        if (it->left_time <= 0.0f) {
          std::string param =
              std::to_string(r.robot_id) + ":" + std::to_string(it->buff_type);
          pushEvent(EventId::EXT_BUFF_EXPIRED, param);
          it = r.buffs.erase(it);
        } else {
          ++it;
        }
      }
    }
  };

  process_team(ally_);
  process_team(enemy_);
}

void GameSimulator::tickOutOfCombat(float dt) {
  auto process_team = [&](TeamState& team) {
    for (int i = 0; i < NUM_ROBOTS; ++i) {
      RobotState& r = team.robots[i];
      if (!r.is_alive) continue;

      r.no_fire_timer += dt;
      r.no_damage_timer += dt;

      bool prev = r.is_out_of_combat;
      r.is_out_of_combat = (r.no_fire_timer >= OUT_OF_COMBAT_TIME &&
                            r.no_damage_timer >= OUT_OF_COMBAT_TIME);

      if (!prev && r.is_out_of_combat) {
        // 脱战后自动远程回血条件
        if (!r.remote_heal_pending && randChance(0.2f)) {
          r.remote_heal_pending = true;
          r.remote_heal_timer = 0.0f;
        }
      }
    }
  };

  process_team(ally_);
  process_team(enemy_);
}

void GameSimulator::tickSupplyZone(float dt) {
  (void)dt;
  auto process_team = [&](TeamState& team, bool is_ally) {
    float base_x = is_ally ? 2.0f : ARENA_WIDTH - 2.0f;
    float min_y = ARENA_HEIGHT * 0.2f;
    float max_y = ARENA_HEIGHT * 0.8f;

    for (int i = 0; i < NUM_ROBOTS; ++i) {
      RobotState& r = team.robots[i];
      if (!r.is_alive) continue;

      bool in_zone = std::fabs(r.pos_x - base_x) < 1.0f && r.pos_y >= min_y &&
                     r.pos_y <= max_y;
      r.in_supply_zone = in_zone;

      if (in_zone) {
        float rate = (match_elapsed_ < SUPPLY_HEAL_LATE_TIME)
                         ? SUPPLY_HEAL_RATE
                         : SUPPLY_HEAL_RATE_LATE;
        uint32_t heal = static_cast<uint32_t>(r.max_health * rate * 0.1f);
        if (heal > 0 && r.current_health < r.max_health) {
          uint32_t old = r.current_health;
          r.current_health = std::min(r.max_health, r.current_health + heal);
          if (r.current_health > old) {
            addBuff(r, BuffType::SUPPLY_HEAL, 1, 3.0f);
          }
        }
      }
    }
  };

  process_team(ally_, true);
  process_team(enemy_, false);
}

void GameSimulator::tickRemoteHeal(float dt) {
  auto process_team = [&](TeamState& team) {
    for (int i = 0; i < NUM_ROBOTS; ++i) {
      RobotState& r = team.robots[i];
      if (!r.remote_heal_pending) continue;

      r.remote_heal_timer += dt;
      if (r.remote_heal_timer >= REMOTE_HEAL_DELAY) {
        r.remote_heal_pending = false;
        r.remote_heal_timer = 0.0f;

        float cost = calcRemoteHealCost();
        if (team.remaining_economy >= static_cast<uint32_t>(cost)) {
          team.remaining_economy -= static_cast<uint32_t>(cost);
          uint32_t heal =
              static_cast<uint32_t>(r.max_health * REMOTE_HEAL_PERCENT);
          uint32_t old = r.current_health;
          r.current_health = std::min(r.max_health, r.current_health + heal);
          if (r.current_health > old) {
            std::string param = std::to_string(r.robot_id) + ":" +
                                std::to_string(static_cast<uint32_t>(cost));
            pushEvent(EventId::EXT_REMOTE_HEAL, param);
          }
        }
      }
    }
  };

  process_team(ally_);
  process_team(enemy_);
}

void GameSimulator::tickPositions(float dt) {
  auto process_team = [&](TeamState& team, bool is_ally) {
    for (int i = 0; i < NUM_ROBOTS; ++i) {
      RobotState& r = team.robots[i];
      if (!r.is_alive) continue;

      r.move_timer -= dt;
      if (r.move_timer <= 0.0f) {
        // 选取新的目标点：靠近中线稍微偏向敌方
        float base_x =
            is_ally ? ARENA_WIDTH * 0.5f + 1.0f : ARENA_WIDTH * 0.5f - 1.0f;
        r.target_x = clampValue(randFloat(base_x - 4.0f, base_x + 4.0f), 1.0f,
                                ARENA_WIDTH - 1.0f);
        r.target_y = clampValue(randFloat(1.0f, ARENA_HEIGHT - 1.0f), 1.0f,
                                ARENA_HEIGHT - 1.0f);
        r.move_timer = randFloat(3.0f, 8.0f);
      }

      float speed = 0.0f;
      switch (r.type) {
        case RobotType::Hero:
          speed = 2.4f;
          break;
        case RobotType::Engineer:
          speed = 1.8f;
          break;
        case RobotType::Infantry3:
        case RobotType::Infantry4:
          speed = 2.1f;
          break;
        case RobotType::Sentry:
          speed = 0.6f;
          break;
      }

      float dx = r.target_x - r.pos_x;
      float dy = r.target_y - r.pos_y;
      float dist = std::sqrt(dx * dx + dy * dy);
      if (dist < kEpsilon) continue;

      float step = speed * dt;
      float t = (step >= dist) ? 1.0f : (step / dist);
      r.pos_x += dx * t;
      r.pos_y += dy * t;

      r.pos_x = clampValue(r.pos_x, 0.5f, ARENA_WIDTH - 0.5f);
      r.pos_y = clampValue(r.pos_y, 0.5f, ARENA_HEIGHT - 0.5f);

      r.yaw = std::atan2(dy, dx);
    }
  };

  process_team(ally_, true);
  process_team(enemy_, false);
}

void GameSimulator::tickExperience(float dt) {
  (void)dt;
  auto process_team = [&](TeamState& team) {
    for (int i = 0; i < NUM_ROBOTS; ++i) {
      RobotState& r = team.robots[i];
      if (!r.is_alive) continue;

      uint32_t old_level = getLevelForExp(r.experience, r.max_level);

      // 持续缓慢获得经验
      r.experience += 4;
      uint32_t new_level = getLevelForExp(r.experience, r.max_level);
      if (new_level > old_level) {
        updatePerformanceStats(r);
        std::string param =
            std::to_string(r.robot_id) + ":" + std::to_string(new_level);
        pushEvent(EventId::EXT_LEVEL_UP, param);
      }
    }
  };

  process_team(ally_);
  process_team(enemy_);
}

void GameSimulator::tickAirSupport(float dt) {
  auto process_team = [&](TeamState& team, bool is_ally) {
    if (!team.air_support_active) {
      if (team.air_support_time_remaining > 0.0f &&
          match_elapsed_ > AIR_SUPPORT_INITIAL_TIME && randChance(dt / 60.0f)) {
        team.air_support_active = true;
        // 官方协议: 10=己方空中支援, 12=对方空中支援
        pushEvent(
            is_ally ? EventId::AIR_SUPPORT_OWN : EventId::AIR_SUPPORT_ENEMY,
            "");
      }
    } else {
      // 激活中
      float cost = AIR_SUPPORT_COST_PER_SEC * dt;
      team.air_support_time_remaining -= dt;
      if (team.air_support_time_remaining < 0.0f)
        team.air_support_time_remaining = 0.0f;
      team.air_support_cost_total += static_cast<uint32_t>(cost);

      if (team.air_support_time_remaining <= 0.0f || randChance(dt / 30.0f)) {
        team.air_support_active = false;
        pushEvent(EventId::EXT_AIR_SUPPORT_END, is_ally ? "red" : "blue");
      }
    }
  };

  process_team(ally_, true);
  process_team(enemy_, false);
}

void GameSimulator::tickRune(float dt) {
  auto process_team = [&](TeamState& team, const char* team_name) {
    RuneState& rs = team.rune;

    float t = match_elapsed_;
    // 官方协议 3: 能量机关可激活次数变化
    // 官方协议 4: 能量机关当前可进入正在激活状态
    if (!rs.small_t0_given && t >= 0.0f) {
      rs.small_t0_given = true;
      rs.small_chances++;
      pushEvent(EventId::RUNE_CHANCE_CHANGE,
                std::to_string(rs.small_chances + rs.large_chances));
      pushEvent(EventId::RUNE_CAN_ACTIVATE, "");
    }
    if (!rs.small_t90_given && t >= 90.0f) {
      rs.small_t90_given = true;
      rs.small_chances++;
      pushEvent(EventId::RUNE_CHANCE_CHANGE,
                std::to_string(rs.small_chances + rs.large_chances));
      pushEvent(EventId::RUNE_CAN_ACTIVATE, "");
    }

    if (!rs.large_t180_given && t >= 180.0f) {
      rs.large_t180_given = true;
      rs.large_chances++;
      pushEvent(EventId::RUNE_CHANCE_CHANGE,
                std::to_string(rs.small_chances + rs.large_chances));
      pushEvent(EventId::RUNE_CAN_ACTIVATE, "");
    }
    if (!rs.large_t255_given && t >= 255.0f) {
      rs.large_t255_given = true;
      rs.large_chances++;
      pushEvent(EventId::RUNE_CHANCE_CHANGE,
                std::to_string(rs.small_chances + rs.large_chances));
      pushEvent(EventId::RUNE_CAN_ACTIVATE, "");
    }
    if (!rs.large_t330_given && t >= 330.0f) {
      rs.large_t330_given = true;
      rs.large_chances++;
      pushEvent(EventId::RUNE_CHANCE_CHANGE,
                std::to_string(rs.small_chances + rs.large_chances));
      pushEvent(EventId::RUNE_CAN_ACTIVATE, "");
    }

    // 简化：每次机会直接成功激活一个 buff
    if (rs.small_chances > 0 && rs.phase == RunePhase::Inactive) {
      rs.phase = RunePhase::Activated;
      rs.current_size = RuneSize::Small;
      rs.attack_bonus = 0.0f;
      rs.defense_bonus = RUNE_SMALL_DEFENSE;
      rs.cooldown_mult = 1.0f;
      rs.buff_remaining = RUNE_SMALL_DURATION;
      rs.small_chances--;
      // 官方协议 6: 能量机关被激活（含激活类型）
      pushEvent(EventId::RUNE_ACTIVATED, "small");
    }

    if (rs.large_chances > 0 && rs.phase == RunePhase::Inactive && t > 210.0f) {
      rs.phase = RunePhase::Activated;
      rs.current_size = RuneSize::Large;
      int idx = clampValue(team.tech_level, 0u, 4u);
      const LargeRuneBuff& b = LARGE_RUNE_BUFFS[idx];
      rs.attack_bonus = b.attack_bonus - 1.0f;
      rs.defense_bonus = b.defense_bonus;
      rs.cooldown_mult = b.cooldown_mult;
      rs.buff_remaining = LARGE_RUNE_DURATION[std::min(5u, team.tech_level)];
      rs.large_chances--;
      // 官方协议 5: 能量机关臂数+平均环数
      pushEvent(EventId::RUNE_ARM_RESULT,
                std::to_string(rs.activated_arms) + ":" +
                    std::to_string(static_cast<int>(rs.average_rings)));
      // 官方协议 6: 能量机关被激活（含激活类型）
      pushEvent(EventId::RUNE_ACTIVATED, "large");
    }

    if (rs.phase == RunePhase::Activated) {
      rs.buff_remaining -= dt;
      if (rs.buff_remaining <= 0.0f) {
        rs.phase = RunePhase::Inactive;
        rs.attack_bonus = 0.0f;
        rs.defense_bonus = 0.0f;
        rs.cooldown_mult = 1.0f;
        pushEvent(EventId::EXT_RUNE_BUFF_EXPIRED, team_name);
      }
    }
  };

  process_team(ally_, "red");
  process_team(enemy_, "blue");
}

void GameSimulator::tickOutpost(float dt) {
  (void)dt;
  auto process_team = [&](TeamState& team, const char* team_name) {
    if (team.outpost_health == 0 && !team.outpost_ever_destroyed) {
      team.outpost_ever_destroyed = true;
      team.outpost_status = UnitStatus::Destroyed;
      // 官方协议 2: 建筑被摧毁 param=目标id（红方前哨站=11, 蓝方前哨站=111）
      std::string target_id = (std::string(team_name) == "red") ? "11" : "111";
      pushEvent(EventId::STRUCTURE_DESTROYED, target_id);
    }
  };

  process_team(ally_, "red");
  process_team(enemy_, "blue");
}

void GameSimulator::tickSentryPosture(float dt) {
  auto process_team = [&](TeamState& team) {
    for (int i = 0; i < NUM_ROBOTS; ++i) {
      RobotState& r = team.robots[i];
      if (r.type != RobotType::Sentry) continue;

      r.posture_switch_cooldown -= dt;
      if (r.posture_switch_cooldown <= 0.0f) {
        // 在攻击/防御/移动之间轮换
        int next = (static_cast<int>(r.posture) + 1) % 3;
        r.posture = static_cast<SentryPosture>(next);
        r.posture_switch_cooldown =
            SENTRY_POSTURE_COOLDOWN + randFloat(0.0f, 3.0f);
        r.posture_weakened =
            (match_elapsed_ >= SENTRY_POSTURE_WEAKEN_TIME && randChance(0.3f));

        pushEvent(EventId::EXT_SENTRY_POSTURE_CHANGE,
                  std::to_string(static_cast<uint32_t>(r.posture)));
      }
    }
  };

  process_team(ally_);
  process_team(enemy_);
}

void GameSimulator::tickSentrySupply(float dt) {
  (void)dt;
  auto process_team = [&](TeamState& team) {
    for (int i = 0; i < NUM_ROBOTS; ++i) {
      RobotState& r = team.robots[i];
      if (r.type != RobotType::Sentry || !r.is_alive) continue;

      if (r.remaining_ammo < 80 && team.sentry_supply_pending == 0) {
        team.sentry_supply_pending = SENTRY_SUPPLY_AMMO;
      }

      if (team.sentry_supply_pending > 0) {
        uint32_t add = std::min(team.sentry_supply_pending, 20u);
        r.remaining_ammo += add;
        team.sentry_supply_pending -= add;
        pushEvent(EventId::EXT_SENTRY_SUPPLY_AMMO, std::to_string(add));
      }
    }
  };

  process_team(ally_);
  process_team(enemy_);
}

void GameSimulator::tickEngineerDefense(float dt) {
  (void)dt;
  if (match_elapsed_ > ENGINEER_EARLY_DEFENSE_TIME) return;

  auto process_team = [&](TeamState& team) {
    for (int i = 0; i < NUM_ROBOTS; ++i) {
      RobotState& r = team.robots[i];
      if (r.type != RobotType::Engineer) continue;

      if (!hasBuff(r, BuffType::ENGINEER_EARLY_DEF)) {
        addBuff(r, BuffType::ENGINEER_EARLY_DEF, 1,
                ENGINEER_EARLY_DEFENSE_TIME - match_elapsed_);
        pushEvent(EventId::EXT_ENGINEER_DEFENSE_START, "");
      }
    }
  };

  process_team(ally_);
  process_team(enemy_);
}

void GameSimulator::tickAssembly(float dt) {
  (void)dt;
  auto process_team = [&](TeamState& team, const char* team_name) {
    if (team.is_assembling) {
      team.assembly_timer += dt;
      if (team.assembly_timer >= 20.0f) {
        team.is_assembling = false;
        team.assembly_timer = 0.0f;
        team.assembly_completions[team.assembling_level]++;
        team.tech_level = std::min<uint32_t>(10, team.tech_level + 1);
        pushEvent(EventId::EXT_TECH_CORE_ASSEMBLED,
                  std::string(team_name) + ":" +
                      std::to_string(team.assembling_level));
      }
    } else {
      if (match_elapsed_ > 60.0f && randChance(0.02f)) {
        team.is_assembling = true;
        team.assembling_level = randInt(0, 3);
        team.assembly_timer = 0.0f;
        pushEvent(EventId::EXT_TECH_CORE_ASSEMBLING,
                  std::string(team_name) + ":" +
                      std::to_string(team.assembling_level));
      }
    }
  };

  process_team(ally_, "red");
  process_team(enemy_, "blue");
}

void GameSimulator::tickFortress(float dt) {
  (void)dt;
  auto process_team = [&](TeamState& team, bool is_ally) {
    FortressState& f = team.fortress;
    if (!f.activated && match_elapsed_ > FORTRESS_ENEMY_TIME) {
      f.activated = true;
      f.reserve_ammo_cap = 500;
      f.reserve_ammo = 200;
      f.heat_cooldown_bonus = 2.0f;
    }
  };

  process_team(ally_, true);
  process_team(enemy_, false);
}

void GameSimulator::tickRadarMark(float dt) {
  auto process_team = [&](TeamState& atk, TeamState& def) {
    for (int i = 0; i < NUM_ROBOTS; ++i) {
      RobotState& target = def.robots[i];
      if (!target.is_alive) continue;

      RadarMarkState& m = target.radar_mark;
      float dir = target.radar_mark.last_was_positive ? -1.0f : 1.0f;
      m.x_value += dir * dt * 5.0f;
      if (std::fabs(m.x_value) > RADAR_P_MAX) {
        m.x_value = RADAR_P_MAX * dir;
        target.radar_mark.last_was_positive =
            !target.radar_mark.last_was_positive;
      }

      m.progress = clampValue(std::fabs(m.x_value), 0.0f, RADAR_P_MAX);
      if (m.progress >= RADAR_P_VULN_1 &&
          atk.radar_double_vuln_active == false) {
        std::string param = std::to_string(target.robot_id) + ":" +
                            std::to_string(static_cast<int>(m.progress));
        pushEvent(EventId::EXT_RADAR_MARK_THRESHOLD, param);
      }

      if (!atk.radar_double_vuln_active &&
          atk.radar_double_vuln_used < RADAR_DOUBLE_VULN_MAX &&
          atk.radar_vuln_accumulate >= RADAR_DOUBLE_VULN_ACCUMULATE) {
        atk.radar_double_vuln_active = true;
        atk.radar_double_vuln_timer = RADAR_DOUBLE_VULN_DURATION;
        atk.radar_double_vuln_used++;
        pushEvent(EventId::EXT_RADAR_DOUBLE_VULN, "");
      }

      if (atk.radar_double_vuln_active) {
        atk.radar_double_vuln_timer -= dt;
        if (atk.radar_double_vuln_timer <= 0.0f) {
          atk.radar_double_vuln_active = false;
          atk.radar_double_vuln_timer = 0.0f;
        }
      }
    }
  };

  tickBuffEffects();

  process_team(ally_, enemy_);
  process_team(enemy_, ally_);
}

void GameSimulator::tickDart(float dt) {
  auto process_team = [&](TeamState& team, TeamState& enemy,
                          const char* team_name) {
    DartState& d = team.dart;

    d.gate_timer += dt;
    d.cooldown_timer -= dt;
    if (d.cooldown_timer < 0.0f) d.cooldown_timer = 0.0f;

    if (d.gate_state == DartGateState::Closed) {
      if (d.gate_opens_remaining > 0 && d.cooldown_timer <= 0.0f &&
          (std::fabs(match_elapsed_ - DART_GATE_OPEN_TIME_1) < 1.0f ||
           std::fabs(match_elapsed_ - DART_GATE_OPEN_TIME_2) < 1.0f)) {
        d.gate_state = DartGateState::Opening;
        d.gate_timer = 0.0f;
        // 官方协议 15: 飞镖闸门开启  param: "1"=己方,"2"=对方
        pushEvent(EventId::DART_GATE,
                  (std::string(team_name) == "red") ? "1" : "2");
      }
    } else if (d.gate_state == DartGateState::Opening) {
      if (d.gate_timer >= 1.0f) {
        d.gate_state = DartGateState::Open;
        d.fire_window_timer = DART_FIRE_WINDOW;
        d.gate_opens_remaining--;
      }
    } else if (d.gate_state == DartGateState::Open) {
      d.fire_window_timer -= dt;
      if (d.fire_window_timer <= 0.0f) {
        d.gate_state = DartGateState::Closed;
        d.cooldown_timer = DART_COOLDOWN;
        pushEvent(EventId::EXT_DART_GATE_CLOSE, team_name);
      } else if (d.darts_remaining > 0 && randChance(dt / 10.0f)) {
        d.darts_remaining--;
        d.current_target = DartTarget::Outpost;
        pushEvent(EventId::EXT_DART_LAUNCHED,
                  std::string(team_name) + ":" + "outpost");

        // 命中效果
        uint32_t dmg = DART_DMG_OUTPOST;
        dealOutpostDamage(enemy, dmg);
        // 官方协议 14: 飞镖命中  param: 1=前哨站
        pushEvent(EventId::DART_HIT, "1");
      }
    }
  };

  process_team(ally_, enemy_, "red");
  process_team(enemy_, ally_, "blue");
}

void GameSimulator::tickChassisEnergy(float dt) {
  auto process_team = [&](TeamState& team) {
    for (int i = 0; i < NUM_ROBOTS; ++i) {
      RobotState& r = team.robots[i];
      if (!r.is_alive) continue;

      // 简单能量模型：缓冲能量慢慢回充，底盘能量随移动消耗
      r.current_buffer_energy =
          std::min(r.max_buffer_energy,
                   r.current_buffer_energy + static_cast<uint32_t>(50 * dt));

      if (r.type != RobotType::Sentry) {
        uint32_t use = static_cast<uint32_t>(30 * dt);
        if (r.current_chassis_energy > use)
          r.current_chassis_energy -= use;
        else
          r.current_chassis_energy = 0;
      }

      bool prev_saving = r.energy_saving;
      bool prev_boost = r.energy_boost;

      r.energy_saving =
          (r.current_chassis_energy <= CHASSIS_POWER_SAVING_LIMIT);
      r.energy_boost =
          (r.current_chassis_energy >= CHASSIS_ENERGY_BOOST_THRESHOLD);

      if (!prev_saving && r.energy_saving) {
        pushEvent(EventId::EXT_ENERGY_SAVING_MODE, std::to_string(r.robot_id));
      }
      if (!prev_boost && r.energy_boost) {
        pushEvent(EventId::EXT_ENERGY_BOOST_MODE, std::to_string(r.robot_id));
      }
    }
  };

  process_team(ally_);
  process_team(enemy_);
}

void GameSimulator::tickBuffEffects() {
  auto process_team = [&](TeamState& team) {
    for (int i = 0; i < NUM_ROBOTS; ++i) {
      RobotState& r = team.robots[i];
      recalcEffectiveStats(r, team);

      for (const BuffState& b : r.buffs) {
        switch (b.buff_type) {
          case BuffType::ATTACK:
            r.eff_attack += 0.25f * b.buff_level;
            break;
          case BuffType::DEFENSE:
            r.eff_defense += 0.10f * b.buff_level;
            break;
          case BuffType::HEAT_COOLDOWN:
            r.eff_cooldown_mult *= 1.0f + 0.25f * b.buff_level;
            break;
          case BuffType::VULNERABILITY:
            r.eff_defense -= 0.20f * b.buff_level;
            break;
          case BuffType::INVINCIBLE:
            r.invincible_timer = std::max(r.invincible_timer, 1.0f);
            break;
          default:
            break;
        }
      }
    }
  };

  process_team(ally_);
  process_team(enemy_);
}

void GameSimulator::dealDamage(RobotState& target, TeamState& target_team,
                               uint32_t damage, uint32_t attacker_id,
                               bool is_17mm, bool is_dart) {
  if (!target.is_alive) return;

  if (target.invincible_timer > 0.0f) return;

  float real_dmg = static_cast<float>(damage) * (1.0f - target.eff_defense);
  if (real_dmg < 1.0f) real_dmg = 1.0f;

  uint32_t dmg_u = static_cast<uint32_t>(real_dmg);
  uint32_t old_hp = target.current_health;
  if (dmg_u >= target.current_health) {
    target.current_health = 0;
  } else {
    target.current_health -= dmg_u;
  }

  target.total_damage += dmg_u;
  if (is_dart) {
    target.dart_splash_damage += dmg_u;
  } else if (is_17mm) {
    target.small_projectile_damage += dmg_u;
  } else {
    target.large_projectile_damage += dmg_u;
  }

  target_team.total_damage_dealt += dmg_u;

  if (target.current_health == 0 && old_hp > 0) {
    killRobot(target, attacker_id);
  }
}

void GameSimulator::dealBaseDamage(TeamState& team, uint32_t damage) {
  if (team.base_health == 0) return;
  if (damage >= team.base_health)
    team.base_health = 0;
  else
    team.base_health -= damage;
}

void GameSimulator::dealOutpostDamage(TeamState& team, uint32_t damage) {
  if (team.outpost_health == 0) return;
  if (damage >= team.outpost_health)
    team.outpost_health = 0;
  else
    team.outpost_health -= damage;
}

void GameSimulator::killRobot(RobotState& r, uint32_t killer_id) {
  if (!r.is_alive) return;

  r.is_alive = false;
  r.is_on_field = false;
  r.is_pending_respawn = true;
  r.respawn_progress = 0.0f;
  r.killer_id = killer_id;

  // 官方协议 1: 击杀事件  param: "killer_id:victim_id"
  std::string param =
      std::to_string(killer_id) + ":" + std::to_string(r.robot_id);
  pushEvent(EventId::KILL, param);

  if (!first_blood_happened_) {
    first_blood_happened_ = true;
    pushEvent(EventId::EXT_FIRST_BLOOD, param);
  }
}

void GameSimulator::respawnRobot(RobotState& r, bool instant) {
  r.is_alive = true;
  r.is_on_field = true;
  r.is_pending_respawn = false;

  r.current_health =
      static_cast<uint32_t>(r.max_health * RESPAWN_HEALTH_PERCENT);
  r.is_weak = true;
  r.power_boost_timer = RESPAWN_POWER_BOOST_DURATION;
  r.invincible_timer =
      instant ? RESPAWN_INVINCIBLE_INSTANT : RESPAWN_INVINCIBLE_NORMAL;
}

uint32_t GameSimulator::calcRespawnGoldCost(const RobotState& r) const {
  // getLevelForExp 非 const，这里手动计算等级
  uint32_t level = 1;
  for (int i = 0; i < 10 && level < r.max_level; ++i) {
    if (r.experience >= EXP_TABLE[i]) level = i + 1;
  }
  level = clampValue(level, 1u, r.max_level);
  return 50u * level;
}

float GameSimulator::calcRemoteHealCost() const { return 100.0f; }

void GameSimulator::addBuff(RobotState& r, uint32_t type, int32_t level,
                            float duration) {
  BuffState b;
  b.buff_type = type;
  b.buff_level = level;
  b.max_time = duration;
  b.left_time = duration;
  r.buffs.push_back(b);
}

void GameSimulator::removeBuff(RobotState& r, uint32_t type) {
  for (auto it = r.buffs.begin(); it != r.buffs.end();) {
    if (it->buff_type == type)
      it = r.buffs.erase(it);
    else
      ++it;
  }
}

bool GameSimulator::hasBuff(const RobotState& r, uint32_t type) const {
  for (const BuffState& b : r.buffs) {
    if (b.buff_type == type) return true;
  }
  return false;
}

void GameSimulator::pushEvent(int32_t event_id, const std::string& param) {
  GameEvent e;
  e.event_id = event_id;
  e.param = param;
  pending_events_.push_back(std::move(e));
}

template <typename T>
std::vector<uint8_t> GameSimulator::serialize(const T& msg) {
  std::vector<uint8_t> buf(msg.ByteSizeLong());
  if (!buf.empty()) {
    msg.SerializeToArray(buf.data(), static_cast<int>(buf.size()));
  }
  return buf;
}

// ═══════════════════════════════════════════════════════════════
// 构造各类 Protobuf 消息
// ═══════════════════════════════════════════════════════════════

std::vector<uint8_t> GameSimulator::buildGameStatus() {
  const float prep_dur = fast_mode_ ? FAST_PREP_DURATION : PREP_DURATION;
  const float self_dur =
      fast_mode_ ? FAST_SELFCHECK_DURATION : SELFCHECK_DURATION;
  const float cd_dur =
      fast_mode_ ? FAST_COUNTDOWN_DURATION : COUNTDOWN_DURATION;
  const float match_dur = fast_mode_ ? FAST_MATCH_DURATION : MATCH_DURATION;

  GameStatus msg;
  msg.set_current_round(current_round_);
  msg.set_total_rounds(total_rounds_);
  msg.set_red_score(red_score_);
  msg.set_blue_score(blue_score_);
  msg.set_current_stage(static_cast<uint32_t>(stage_));

  int32_t stage_cd = 0;
  if (stage_ == GameStage::Preparation)
    stage_cd = static_cast<int32_t>(std::max(0.0f, prep_dur - stage_timer_));
  else if (stage_ == GameStage::SelfCheck)
    stage_cd = static_cast<int32_t>(std::max(0.0f, self_dur - stage_timer_));
  else if (stage_ == GameStage::Countdown)
    stage_cd = static_cast<int32_t>(std::max(0.0f, cd_dur - stage_timer_));
  else if (stage_ == GameStage::InProgress)
    stage_cd = static_cast<int32_t>(std::max(0.0f, match_dur - match_elapsed_));

  msg.set_stage_countdown_sec(stage_cd);
  msg.set_stage_elapsed_sec(static_cast<int32_t>(stage_timer_));
  msg.set_is_paused(false);

  return serialize(msg);
}

std::vector<uint8_t> GameSimulator::buildGlobalUnitStatus() {
  GlobalUnitStatus msg;

  msg.set_base_health(ally_.base_health);
  msg.set_base_status(static_cast<uint32_t>(ally_.base_status));
  msg.set_base_shield(ally_.base_shield);

  msg.set_outpost_health(ally_.outpost_health);
  msg.set_outpost_status(static_cast<uint32_t>(ally_.outpost_status));

  msg.set_enemy_base_health(enemy_.base_health);
  msg.set_enemy_base_status(static_cast<uint32_t>(enemy_.base_status));
  msg.set_enemy_base_shield(enemy_.base_shield);

  msg.set_enemy_outpost_health(enemy_.outpost_health);
  msg.set_enemy_outpost_status(static_cast<uint32_t>(enemy_.outpost_status));

  for (int i = 0; i < NUM_ROBOTS; ++i) {
    msg.add_robot_health(ally_.robots[i].current_health);
  }
  for (int i = 0; i < NUM_ROBOTS; ++i) {
    msg.add_robot_bullets(ally_.robots[i].remaining_ammo);
  }

  msg.set_total_damage_ally(ally_.total_damage_dealt);
  msg.set_total_damage_enemy(enemy_.total_damage_dealt);

  return serialize(msg);
}

std::vector<uint8_t> GameSimulator::buildGlobalLogisticsStatus() {
  GlobalLogisticsStatus msg;
  msg.set_remaining_economy(ally_.remaining_economy);
  msg.set_total_economy_obtained(
      static_cast<uint32_t>(ally_.total_economy_obtained));
  msg.set_tech_level(ally_.tech_level);
  msg.set_encryption_level(ally_.encryption_level);
  return serialize(msg);
}

std::vector<uint8_t> GameSimulator::buildGlobalSpecialMechanism() {
  GlobalSpecialMechanism msg;
  // 简单填充 3 个机制：能量机关 / 堡垒 / 飞镖
  msg.add_mechanism_id(1);
  msg.add_mechanism_id(2);
  msg.add_mechanism_id(3);
  msg.add_mechanism_time_sec(static_cast<int32_t>(ally_.rune.buff_remaining));
  msg.add_mechanism_time_sec(static_cast<int32_t>(FORTRESS_ENEMY_TIME));
  msg.add_mechanism_time_sec(
      static_cast<int32_t>(ally_.dart.fire_window_timer));
  return serialize(msg);
}

std::vector<uint8_t> GameSimulator::buildEvent() {
  Event msg;
  if (!pending_events_.empty()) {
    GameEvent e = pending_events_.front();
    pending_events_.erase(pending_events_.begin());
    msg.set_event_id(e.event_id);
    msg.set_param(e.param);
  } else {
    msg.set_event_id(0);
    msg.set_param("");
  }
  return serialize(msg);
}

std::vector<uint8_t> GameSimulator::buildRobotInjuryStat() {
  RobotInjuryStat msg;
  const RobotState& r = ally_.robots[self_robot_idx_];
  msg.set_total_damage(r.total_damage);
  msg.set_collision_damage(r.collision_damage);
  msg.set_small_projectile_damage(r.small_projectile_damage);
  msg.set_large_projectile_damage(r.large_projectile_damage);
  msg.set_dart_splash_damage(r.dart_splash_damage);
  msg.set_module_offline_damage(r.module_offline_damage);
  msg.set_offline_damage(r.offline_damage);
  msg.set_penalty_damage(r.penalty_damage);
  msg.set_server_kill_damage(r.server_kill_damage);
  msg.set_killer_id(r.killer_id);
  return serialize(msg);
}

std::vector<uint8_t> GameSimulator::buildRobotRespawnStatus() {
  RobotRespawnStatus msg;
  const RobotState& r = ally_.robots[self_robot_idx_];
  msg.set_is_pending_respawn(r.is_pending_respawn);
  msg.set_total_respawn_progress(
      static_cast<uint32_t>(r.total_respawn_progress));
  msg.set_current_respawn_progress(static_cast<uint32_t>(r.respawn_progress));
  msg.set_can_free_respawn(!r.is_pending_respawn);
  uint32_t cost = calcRespawnGoldCost(r);
  msg.set_gold_cost_for_respawn(cost);
  msg.set_can_pay_for_respawn(ally_.remaining_economy >= cost);
  return serialize(msg);
}

std::vector<uint8_t> GameSimulator::buildRobotStaticStatus() {
  RobotStaticStatus msg;
  const RobotState& r = ally_.robots[self_robot_idx_];
  msg.set_connection_state(r.is_connected ? 1u : 0u);
  msg.set_field_state(r.is_on_field ? 1u : 0u);
  msg.set_alive_state(r.is_alive ? 1u : 0u);
  msg.set_robot_id(r.robot_id);
  msg.set_robot_type(static_cast<uint32_t>(r.type));
  msg.set_performance_system_shooter(r.shooter_type);
  msg.set_performance_system_chassis(r.chassis_type);
  msg.set_level(getLevelForExp(r.experience, r.max_level));
  msg.set_max_health(r.max_health);
  msg.set_max_heat(r.max_heat);
  msg.set_heat_cooldown_rate(r.base_cooldown_rate);
  msg.set_max_power(r.max_power);
  msg.set_max_buffer_energy(r.max_buffer_energy);
  msg.set_max_chassis_energy(r.max_chassis_energy);
  return serialize(msg);
}

std::vector<uint8_t> GameSimulator::buildRobotDynamicStatus() {
  RobotDynamicStatus msg;
  const RobotState& r = ally_.robots[self_robot_idx_];
  msg.set_current_health(r.current_health);
  msg.set_current_heat(r.current_heat);
  msg.set_last_projectile_fire_rate(r.last_fire_speed);
  msg.set_current_chassis_energy(r.current_chassis_energy);
  msg.set_current_buffer_energy(r.current_buffer_energy);
  msg.set_current_experience(r.experience);
  uint32_t lvl = getLevelForExp(r.experience, r.max_level);
  uint32_t next = (lvl < r.max_level) ? EXP_TABLE[lvl] : EXP_TABLE[lvl - 1];
  msg.set_experience_for_upgrade(next);
  msg.set_total_projectiles_fired(r.total_projectiles_fired);
  msg.set_remaining_ammo(
      static_cast<uint32_t>(r.remaining_ammo > 0 ? r.remaining_ammo : 0));
  msg.set_is_out_of_combat(r.is_out_of_combat);

  float remain = std::max(
      0.0f, OUT_OF_COMBAT_TIME - std::min(r.no_fire_timer, r.no_damage_timer));
  msg.set_out_of_combat_countdown(static_cast<uint32_t>(remain));
  msg.set_can_remote_heal(!r.remote_heal_pending && r.is_out_of_combat);
  return serialize(msg);
}

std::vector<uint8_t> GameSimulator::buildRobotModuleStatus() {
  RobotModuleStatus msg;
  const RobotState& r = ally_.robots[self_robot_idx_];
  msg.set_power_manager(r.mod_power_manager);
  msg.set_rfid(r.mod_rfid);
  msg.set_light_strip(r.mod_light_strip);
  msg.set_small_shooter(r.mod_small_shooter);
  msg.set_big_shooter(r.mod_big_shooter);
  msg.set_uwb(r.mod_uwb);
  msg.set_armor(r.mod_armor);
  msg.set_video_transmission(r.mod_video);
  msg.set_capacitor(r.mod_capacitor);
  msg.set_main_controller(r.mod_main_controller);
  msg.set_laser_detection_module(r.mod_laser_detection);
  return serialize(msg);
}

std::vector<uint8_t> GameSimulator::buildRobotPosition() {
  RobotPosition msg;
  const RobotState& r = ally_.robots[self_robot_idx_];
  msg.set_x(r.pos_x);
  msg.set_y(r.pos_y);
  msg.set_z(r.pos_z);
  msg.set_yaw(r.yaw);
  return serialize(msg);
}

std::vector<uint8_t> GameSimulator::buildBuff() {
  Buff msg;
  const RobotState& r = ally_.robots[self_robot_idx_];
  if (!r.buffs.empty()) {
    // 轮流发送每个buff，确保客户端收到所有buff
    uint32_t idx = buff_send_index_ % static_cast<uint32_t>(r.buffs.size());
    buff_send_index_++;
    const BuffState& b = r.buffs[idx];
    msg.set_robot_id(r.robot_id);
    msg.set_buff_type(b.buff_type);
    msg.set_buff_level(b.buff_level);
    msg.set_buff_max_time(static_cast<uint32_t>(b.max_time));
    msg.set_buff_left_time(static_cast<uint32_t>(std::max(0.0f, b.left_time)));
  } else {
    msg.set_robot_id(r.robot_id);
    msg.set_buff_type(0);
    msg.set_buff_level(0);
    msg.set_buff_max_time(0);
    msg.set_buff_left_time(0);
    buff_send_index_ = 0;
  }
  return serialize(msg);
}

std::vector<uint8_t> GameSimulator::buildAllBuffs() {
  // 用于批量发送所有buff（预留接口）
  return buildBuff();
}

std::vector<uint8_t> GameSimulator::buildPenaltyInfo() {
  PenaltyInfo msg;
  // 简化：按总判罚伤害估算
  const RobotState& r = ally_.robots[self_robot_idx_];
  msg.set_penalty_type(0);
  msg.set_penalty_effect_sec(0);
  msg.set_total_penalty_num(static_cast<uint32_t>(r.penalty_damage / 50));
  return serialize(msg);
}

std::vector<uint8_t> GameSimulator::buildRobotPathPlanInfo() {
  RobotPathPlanInfo msg;
  const RobotState& r = ally_.robots[self_robot_idx_];
  msg.set_intention(1);
  msg.set_start_pos_x(static_cast<uint32_t>(r.pos_x * 100));
  msg.set_start_pos_y(static_cast<uint32_t>(r.pos_y * 100));
  for (int i = 0; i < 3; ++i) {
    msg.add_offset_x(static_cast<int32_t>((r.target_x - r.pos_x) * 30));
    msg.add_offset_y(static_cast<int32_t>((r.target_y - r.pos_y) * 30));
  }
  msg.set_sender_id(r.robot_id);
  return serialize(msg);
}

std::vector<uint8_t> GameSimulator::buildRadarInfoToClient() {
  RadarInfoToClient msg;
  const RobotState& self = ally_.robots[self_robot_idx_];
  msg.set_target_robot_id(self.robot_id);
  msg.set_target_pos_x(self.pos_x);
  msg.set_target_pos_y(self.pos_y);
  msg.set_torward_angle(self.yaw);
  msg.set_is_high_light(true);
  return serialize(msg);
}

std::vector<uint8_t> GameSimulator::buildTechCoreMotionStateSync() {
  TechCoreMotionStateSync msg;
  msg.set_maximum_difficulty_level(ally_.team_max_level);
  msg.set_status(ally_.is_assembling ? 1u : 0u);
  msg.set_enemy_core_status(enemy_.is_assembling ? 1u : 0u);
  msg.set_remain_time_all(
      static_cast<uint32_t>(std::max(0.0f, MATCH_DURATION - match_elapsed_)));
  msg.set_remain_time_step(
      static_cast<uint32_t>(std::max(0.0f, 20.0f - ally_.assembly_timer)));
  return serialize(msg);
}

std::vector<uint8_t> GameSimulator::buildRobotPerformanceSelectionSync() {
  RobotPerformanceSelectionSync msg;
  const RobotState& r = ally_.robots[self_robot_idx_];
  msg.set_shooter(r.shooter_type);
  msg.set_chassis(r.chassis_type);
  msg.set_sentry_control(static_cast<uint32_t>(r.sentry_ctrl));
  return serialize(msg);
}

std::vector<uint8_t> GameSimulator::buildDeployModeStatusSync() {
  DeployModeStatusSync msg;
  msg.set_status(0u);
  return serialize(msg);
}

std::vector<uint8_t> GameSimulator::buildRuneStatusSync() {
  RuneStatusSync msg;
  const RuneState& rs = ally_.rune;
  msg.set_rune_status(static_cast<uint32_t>(rs.phase));
  msg.set_activated_arms(rs.activated_arms);
  msg.set_average_rings(static_cast<uint32_t>(rs.average_rings));
  return serialize(msg);
}

std::vector<uint8_t> GameSimulator::buildSentryStatusSync() {
  SentryStatusSync msg;
  // 默认为队伍中第一个哨兵
  const RobotState* sentry = nullptr;
  for (int i = 0; i < NUM_ROBOTS; ++i) {
    if (ally_.robots[i].type == RobotType::Sentry) {
      sentry = &ally_.robots[i];
      break;
    }
  }
  if (!sentry) sentry = &ally_.robots[self_robot_idx_];

  msg.set_posture_id(static_cast<uint32_t>(sentry->posture));
  msg.set_is_weakened(sentry->posture_weakened);
  return serialize(msg);
}

std::vector<uint8_t> GameSimulator::buildDartSelectTargetStatusSync() {
  DartSelectTargetStatusSync msg;
  const DartState& d = ally_.dart;
  msg.set_target_id(static_cast<uint32_t>(d.current_target));
  msg.set_open(static_cast<uint32_t>(d.gate_state == DartGateState::Open));
  return serialize(msg);
}

std::vector<uint8_t> GameSimulator::buildSentryCtrlResult() {
  SentryCtrlResult msg;
  msg.set_command_id(1u);
  msg.set_result_code(0u);
  return serialize(msg);
}

std::vector<uint8_t> GameSimulator::buildAirSupportStatusSync() {
  AirSupportStatusSync msg;
  msg.set_airsupport_status(ally_.air_support_active ? 1u : 0u);
  msg.set_left_time(
      static_cast<uint32_t>(std::max(0.0f, ally_.air_support_time_remaining)));
  msg.set_cost_coins(ally_.air_support_cost_total);
  msg.set_is_being_targeted(0u);
  msg.set_shooter_status(0u);
  return serialize(msg);
}

std::vector<uint8_t> GameSimulator::buildMessageForTopic(
    const std::string& topic) {
  if (topic == "GameStatus") return buildGameStatus();
  if (topic == "GlobalUnitStatus") return buildGlobalUnitStatus();
  if (topic == "GlobalLogisticsStatus") return buildGlobalLogisticsStatus();
  if (topic == "GlobalSpecialMechanism") return buildGlobalSpecialMechanism();
  if (topic == "Event") return buildEvent();
  if (topic == "RobotInjuryStat") return buildRobotInjuryStat();
  if (topic == "RobotRespawnStatus") return buildRobotRespawnStatus();
  if (topic == "RobotStaticStatus") return buildRobotStaticStatus();
  if (topic == "RobotDynamicStatus") return buildRobotDynamicStatus();
  if (topic == "RobotModuleStatus") return buildRobotModuleStatus();
  if (topic == "RobotPosition") return buildRobotPosition();
  if (topic == "Buff") return buildBuff();
  if (topic == "PenaltyInfo") return buildPenaltyInfo();
  if (topic == "RobotPathPlanInfo") return buildRobotPathPlanInfo();
  if (topic == "RadarInfoToClient") return buildRadarInfoToClient();
  if (topic == "TechCoreMotionStateSync") return buildTechCoreMotionStateSync();
  if (topic == "RobotPerformanceSelectionSync")
    return buildRobotPerformanceSelectionSync();
  if (topic == "DeployModeStatusSync") return buildDeployModeStatusSync();
  if (topic == "RuneStatusSync") return buildRuneStatusSync();
  if (topic == "SentryStatusSync") return buildSentryStatusSync();
  if (topic == "DartSelectTargetStatusSync")
    return buildDartSelectTargetStatusSync();
  if (topic == "SentryCtrlResult") return buildSentryCtrlResult();
  if (topic == "AirSupportStatusSync") return buildAirSupportStatusSync();
  if (topic == "CustomByteBlock") {
    CustomByteBlock msg;
    std::string data;
    data.resize(16);
    for (auto& ch : data) ch = static_cast<char>(randInt(0, 255));
    msg.set_data(data);
    return serialize(msg);
  }

  return {};
}
