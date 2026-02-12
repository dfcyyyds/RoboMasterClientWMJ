using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using Framework.Network;
using UI.Core;

namespace UI.HUD
{
    /// <summary>
    /// 事件通知服务（MonoBehaviour）：
    /// 1. MQTT 后台线程通过 ConcurrentQueue 入队事件
    /// 2. Unity 主线程 Update() 处理队列、推送 NotificationHUD
    /// 3. 事件 ID 体系：官方协议 1-18 + 仿真扩展 101+，无冲突
    /// </summary>
    public class EventNotificationService : MonoBehaviour
    {
        public static EventNotificationService Instance { get; private set; }

        // ─── 线程安全队列 ───
        private readonly ConcurrentQueue<QueuedEvent> pendingEvents = new();
        private readonly ConcurrentQueue<QueuedPenalty> pendingPenalties = new();

        // ─── 缓存 ───
        private NotificationHUD cachedHUD;
        private BattleHUD cachedBattleHUD;

        // ─── 防刷：同类事件最小间隔（秒）───
        private readonly Dictionary<int, float> eventCooldown = new();
        private const float DEFAULT_COOLDOWN = 0.3f;
        private const float SPAM_COOLDOWN = 2.0f;

        // ─── 每帧处理上限 ───
        private const int MAX_PER_FRAME = 8;

        // ─── 自定义事件回调（供外部模块扩展） ───
        /// <summary>
        /// 当收到未被内置处理覆盖的事件时触发。
        /// 参数：(eventId, param)
        /// </summary>
        public static event Action<int, string> OnCustomEvent;

        /// <summary>
        /// 当收到判罚信息时触发。
        /// 参数：(penaltyType, effectSec, totalNum)
        /// </summary>
        public static event Action<uint, uint, uint> OnPenaltyReceived;

        // ─── 数据结构 ───

        private struct QueuedEvent
        {
            public int eventId;
            public string param;
        }

        private struct QueuedPenalty
        {
            public uint penaltyType;
            public uint effectSec;
            public uint totalNum;
        }

        // ═══════════════════════════════════════════════════════════════
        // 生命周期
        // ═══════════════════════════════════════════════════════════════

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            ProtobufManager.Instance.OnDataUpdated += OnDataUpdatedFromBackground;
            wmj.Log.I("[EventNotificationService] 已启动，监听 Event / PenaltyInfo", wmj.Log.Tag.Network);
        }

        void OnDestroy()
        {
            ProtobufManager.Instance.OnDataUpdated -= OnDataUpdatedFromBackground;
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            // 惰性获取 HUD 引用（主线程安全）
            if (cachedHUD == null)
            {
                if (cachedBattleHUD == null)
                    cachedBattleHUD = BattleHUD.Instance;
                if (cachedBattleHUD != null)
                    cachedHUD = cachedBattleHUD.NotificationHUD;
            }

            // 处理事件队列
            int processed = 0;
            while (processed < MAX_PER_FRAME && pendingEvents.TryDequeue(out var ev))
            {
                ProcessEvent(ev.eventId, ev.param);
                processed++;
            }

            // 处理判罚队列
            while (pendingPenalties.TryDequeue(out var pen))
            {
                ProcessPenalty(pen.penaltyType, pen.effectSec, pen.totalNum);
            }

            // 冷却衰减
            if (eventCooldown.Count > 0)
            {
                var keys = new List<int>(eventCooldown.Keys);
                float dt = Time.unscaledDeltaTime;
                foreach (var k in keys)
                {
                    eventCooldown[k] -= dt;
                    if (eventCooldown[k] <= 0f)
                        eventCooldown.Remove(k);
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 后台线程回调（仅入队，绝不调用 Unity API）
        // ═══════════════════════════════════════════════════════════════

        private void OnDataUpdatedFromBackground(string typeName, object data)
        {
            try
            {
                if (typeName == nameof(Event) && data is Event ev)
                {
                    if (ev.EventId == 0) return;
                    pendingEvents.Enqueue(new QueuedEvent
                    {
                        eventId = ev.EventId,
                        param = ev.Param ?? string.Empty
                    });
                }
                else if (typeName == nameof(PenaltyInfo) && data is PenaltyInfo pen)
                {
                    if (pen.PenaltyType == 0 && pen.TotalPenaltyNum == 0) return;
                    pendingPenalties.Enqueue(new QueuedPenalty
                    {
                        penaltyType = pen.PenaltyType,
                        effectSec = pen.PenaltyEffectSec,
                        totalNum = pen.TotalPenaltyNum
                    });
                }
            }
            catch (Exception) { /* 后台线程静默 */ }
        }

        // ═══════════════════════════════════════════════════════════════
        // 主线程事件处理（统一 ID 体系）
        // ═══════════════════════════════════════════════════════════════

        private void ProcessEvent(int eventId, string param)
        {
            // 防刷检查
            if (eventCooldown.TryGetValue(eventId, out float cd) && cd > 0f)
                return;

            bool handled = HandleEvent(eventId, param);

            if (!handled)
            {
                PushNotification($"事件[{eventId}]: {param}", UIColors.Silver);
                OnCustomEvent?.Invoke(eventId, param);
            }
        }

        private void ProcessPenalty(uint penaltyType, uint effectSec, uint totalNum)
        {
            if (cachedHUD == null) return;

            string typeName = penaltyType switch
            {
                1 => "黄牌",
                2 => "双方黄牌",
                3 => "红牌",
                4 => "超功率",
                5 => "超热量",
                6 => "超射速",
                _ => $"判罚({penaltyType})"
            };

            Color color = penaltyType switch
            {
                3 => UIColors.Red,
                1 or 2 => UIColors.HeatYellow,
                _ => UIColors.Orange
            };

            string msg = $"[!] {typeName}  持续{effectSec}s  累计{totalNum}次";
            cachedHUD.Push(msg, color, UILayoutManager.Settings.notificationDuration + 1f);
            OnPenaltyReceived?.Invoke(penaltyType, effectSec, totalNum);
        }

        // ═══════════════════════════════════════════════════════════════
        // 统一事件处理（官方 1-18 + 仿真扩展 101+）
        // ═══════════════════════════════════════════════════════════════

        private bool HandleEvent(int id, string param)
        {
            string msg;
            Color color;
            float duration = -1f;
            float cooldown = DEFAULT_COOLDOWN;

            switch (id)
            {
                // ═══════════════════════════════════════════
                // 官方协议 event_id (1-18)
                // ═══════════════════════════════════════════

                case EventIds.KILL:
                    {
                        // param: "killer_id:victim_id"
                        var parts = param.Split(':');
                        msg = parts.Length >= 2
                            ? $"💀 机器人{parts[1]} 被 {parts[0]} 击毁"
                            : $"💀 击杀事件: {param}";
                        color = UIColors.Red;
                        break;
                    }
                case EventIds.STRUCTURE_DESTROYED:
                    {
                        // param: 目标 id（如蓝方前哨站=111）
                        string target = param switch
                        {
                            "11" => "红方前哨站",
                            "111" => "蓝方前哨站",
                            _ => $"建筑(ID={param})"
                        };
                        msg = $"💥 {target}被摧毁！";
                        color = UIColors.Red;
                        duration = 3f;
                        break;
                    }
                case EventIds.RUNE_CHANCE_CHANGE:
                    msg = $"能量机关可激活次数 → {param}";
                    color = UIColors.BrightBlue;
                    cooldown = SPAM_COOLDOWN;
                    break;
                case EventIds.RUNE_CAN_ACTIVATE:
                    msg = "能量机关可进入激活状态";
                    color = UIColors.BrightBlue;
                    break;
                case EventIds.RUNE_ARM_RESULT:
                    {
                        // param: "arms:rings"
                        var parts = param.Split(':');
                        msg = parts.Length >= 2
                            ? $"能量机关激活  臂数:{parts[0]} 均环:{parts[1]}"
                            : $"能量机关激活结果: {param}";
                        color = UIColors.HealthGreen;
                        break;
                    }
                case EventIds.RUNE_ACTIVATED:
                    {
                        // param: "small"/"large" 或激活类型
                        string typeName = param switch
                        {
                            "small" => "小能量机关",
                            "large" => "大能量机关",
                            _ => $"能量机关({param})"
                        };
                        msg = $"✦ {typeName}激活成功";
                        color = UIColors.HealthGreen;
                        duration = 3f;
                        break;
                    }
                case EventIds.HERO_DEPLOY_MODE:
                    msg = "己方英雄进入部署模式";
                    color = UIColors.BrightBlue;
                    break;
                case EventIds.HERO_SNIPER_OWN:
                    msg = $"己方英雄狙击伤害: 累计{param}";
                    color = UIColors.HealthGreen;
                    cooldown = SPAM_COOLDOWN;
                    break;
                case EventIds.HERO_SNIPER_ENEMY:
                    msg = $"[!] 对方英雄狙击伤害: 累计{param}";
                    color = UIColors.Red;
                    cooldown = SPAM_COOLDOWN;
                    break;
                case EventIds.AIR_SUPPORT_OWN:
                    msg = "✈ 己方呼叫空中支援";
                    color = UIColors.BrightBlue;
                    break;
                case EventIds.AIR_SUPPORT_OWN_BROKEN:
                    msg = $"己方空中支援被打断（对方剩余{param}次）";
                    color = UIColors.Red;
                    break;
                case EventIds.AIR_SUPPORT_ENEMY:
                    msg = "[!] 对方呼叫空中支援";
                    color = UIColors.Orange;
                    break;
                case EventIds.AIR_SUPPORT_ENEMY_BROKEN:
                    msg = $"对方空中支援被打断（己方剩余{param}次）";
                    color = UIColors.HealthGreen;
                    break;
                case EventIds.DART_HIT:
                    {
                        string target = param switch
                        {
                            "1" => "前哨站",
                            "2" => "基地（固定目标）",
                            "3" => "基地（随机固定目标）",
                            "4" => "基地（随机移动目标）",
                            "5" => "基地（末端移动目标）",
                            _ => $"目标({param})"
                        };
                        msg = $"🎯 飞镖命中 {target}";
                        color = UIColors.Orange;
                        duration = 3f;
                        break;
                    }
                case EventIds.DART_GATE:
                    msg = param switch
                    {
                        "1" => "己方飞镖闸门开启",
                        "2" => "对方飞镖闸门开启",
                        _ => "飞镖闸门开启"
                    };
                    color = UIColors.Orange;
                    break;
                case EventIds.BASE_ATTACKED:
                    msg = "[!] 己方基地遭到攻击！";
                    color = UIColors.Red;
                    duration = 2f;
                    cooldown = 5f; // 官方：5s 内置冷却
                    break;
                case EventIds.OUTPOST_STOP_ROTATE:
                    msg = param switch
                    {
                        "1" => "己方前哨站装甲停转",
                        "2" => "对方前哨站装甲停转",
                        _ => "前哨站停转"
                    };
                    color = UIColors.Orange;
                    break;
                case EventIds.BASE_ARMOR_OPEN:
                    msg = param switch
                    {
                        "1" => "[!] 己方基地护甲展开！",
                        "2" => "[!] 对方基地护甲展开",
                        _ => "基地护甲展开"
                    };
                    color = UIColors.Red;
                    duration = 3f;
                    break;

                // ═══════════════════════════════════════════
                // 仿真扩展 event_id (101+)
                // ═══════════════════════════════════════════

                // ─── 比赛流程 (101-109) ───
                case EventIds.EXT_MATCH_START:
                    msg = "⚔ 比赛开始！";
                    color = UIColors.White;
                    duration = 3f;
                    break;
                case EventIds.EXT_MATCH_END:
                    msg = param switch
                    {
                        "red_win" => "🏆 比赛结束：红方胜利",
                        "blue_win" => "🏆 比赛结束：蓝方胜利",
                        "draw" => "比赛结束：平局",
                        _ => "比赛结束"
                    };
                    color = UIColors.HeatYellow;
                    duration = 5f;
                    break;
                case EventIds.EXT_STAGE_CHANGE:
                    {
                        string stageName = param switch
                        {
                            "Preparation" => "准备阶段",
                            "SelfCheck" => "自检阶段",
                            "Countdown" => "倒计时",
                            "InProgress" => "比赛进行中",
                            "Ended" => "已结束",
                            _ => param
                        };
                        msg = $"阶段切换 → {stageName}";
                        color = UIColors.BrightBlue;
                        duration = 2f;
                        break;
                    }
                case EventIds.EXT_MATCH_PAUSED:
                    msg = "⏸ 比赛暂停";
                    color = UIColors.HeatYellow;
                    break;
                case EventIds.EXT_MATCH_RESUMED:
                    msg = "▶ 比赛恢复";
                    color = UIColors.HealthGreen;
                    break;

                // ─── 机器人生死 (110-119) ───
                case EventIds.EXT_ROBOT_RESPAWN:
                    msg = $"机器人{param} 已复活";
                    color = UIColors.HealthGreen;
                    cooldown = SPAM_COOLDOWN;
                    break;
                case EventIds.EXT_ROBOT_INSTANT_RESPAWN:
                    {
                        var parts = param.Split(':');
                        msg = parts.Length >= 2
                            ? $"⚡ 机器人{parts[0]} 金币复活（花费{parts[1]}金）"
                            : "⚡ 机器人立即复活";
                        color = UIColors.HeatYellow;
                        break;
                    }
                case EventIds.EXT_ROBOT_OFFLINE:
                    msg = $"[!] 机器人{param} 异常离线";
                    color = UIColors.Red;
                    break;
                case EventIds.EXT_ROBOT_RECONNECT:
                    msg = $"机器人{param} 重新连接";
                    color = UIColors.HealthGreen;
                    break;

                // ─── 前哨站/基地 (120-129) ───
                case EventIds.EXT_OUTPOST_REBUILT:
                    msg = $"{TeamName(param)}前哨站已重建";
                    color = UIColors.HealthGreen;
                    break;
                case EventIds.EXT_BASE_DESTROYED:
                    msg = $"💥 {TeamName(param)}基地被击毁！";
                    color = UIColors.Red;
                    duration = 5f;
                    break;

                // ─── 能量机关 (130-139) ───
                case EventIds.EXT_RUNE_BUFF_EXPIRED:
                    msg = $"{TeamName(param)}能量机关增益结束";
                    color = UIColors.Silver;
                    cooldown = SPAM_COOLDOWN;
                    break;

                // ─── 科技核心 (140-149) ───
                case EventIds.EXT_TECH_CORE_ASSEMBLING:
                    {
                        var parts = param.Split(':');
                        string diff = parts.Length >= 2 ? $" 难度{parts[1]}" : "";
                        msg = $"⚙ {TeamName(parts.Length > 0 ? parts[0] : param)}开始装配科技核心{diff}";
                        color = UIColors.BrightBlue;
                        break;
                    }
                case EventIds.EXT_TECH_CORE_ASSEMBLED:
                    {
                        var parts = param.Split(':');
                        string diff = parts.Length >= 2 ? $" 难度{parts[1]}" : "";
                        msg = $"✦ {TeamName(parts.Length > 0 ? parts[0] : param)}科技核心装配成功{diff}";
                        color = UIColors.HealthGreen;
                        break;
                    }
                case EventIds.EXT_TECH_CORE_FAILED:
                    {
                        var parts = param.Split(':');
                        msg = $"{TeamName(parts.Length > 0 ? parts[0] : param)}科技核心装配失败";
                        color = UIColors.Red;
                        break;
                    }
                case EventIds.EXT_TECH_CORE_LEVEL_UP:
                    {
                        var parts = param.Split(':');
                        msg = parts.Length >= 2
                            ? $"{TeamName(parts[0])}队伍等级上限提升至 {parts[1]} 级"
                            : "队伍等级上限提升";
                        color = UIColors.BrightBlue;
                        break;
                    }

                // ─── 飞镖扩展 (150-159) ───
                case EventIds.EXT_DART_LAUNCHED:
                    {
                        var parts = param.Split(':');
                        string target = parts.Length >= 2 ? DartTargetName(parts[1]) : "目标";
                        msg = $"🚀 {TeamName(parts.Length > 0 ? parts[0] : param)}飞镖发射 → {target}";
                        color = UIColors.Orange;
                        break;
                    }
                case EventIds.EXT_DART_GATE_CLOSE:
                    msg = $"{TeamName(param)}飞镖闸门关闭";
                    color = UIColors.Silver;
                    break;
                case EventIds.EXT_DART_SCREEN_BLOCKED:
                    {
                        var parts = param.Split(':');
                        string dur = parts.Length >= 2 ? $"{parts[1]}s" : "";
                        msg = $"[!] 飞镖命中导致视野遮挡 {dur}";
                        color = UIColors.Red;
                        break;
                    }

                // ─── 空中支援扩展 (160-169) ───
                case EventIds.EXT_AIR_SUPPORT_END:
                    msg = $"{TeamName(param)}空中支援结束";
                    color = UIColors.Silver;
                    break;
                case EventIds.EXT_AERIAL_LOCKED:
                    {
                        var parts = param.Split(':');
                        msg = parts.Length >= 2
                            ? $"[!] 空中机器人{parts[0]} 被锁定 {parts[1]}s"
                            : "空中机器人被雷达锁定";
                        color = UIColors.Red;
                        break;
                    }
                case EventIds.EXT_AERIAL_LOCK_RELEASED:
                    msg = $"空中机器人{param} 锁定解除";
                    color = UIColors.HealthGreen;
                    break;

                // ─── 判罚 (170-179) ───
                case EventIds.EXT_PENALTY_YELLOW:
                    {
                        var parts = param.Split(':');
                        msg = parts.Length >= 2
                            ? $"[!] 黄牌警告：机器人{parts[0]}，{parts[1]}"
                            : "[!] 黄牌警告";
                        color = UIColors.HeatYellow;
                        duration = 3f;
                        break;
                    }
                case EventIds.EXT_PENALTY_RED:
                    {
                        var parts = param.Split(':');
                        msg = parts.Length >= 2
                            ? $"🔴 红牌：机器人{parts[0]}，{parts[1]}"
                            : "🔴 红牌警告";
                        color = UIColors.Red;
                        duration = 4f;
                        break;
                    }
                case EventIds.EXT_PENALTY_WARNING:
                    {
                        var parts = param.Split(':');
                        msg = parts.Length >= 2
                            ? $"口头警告：机器人{parts[0]}，{parts[1]}"
                            : "口头警告";
                        color = UIColors.Orange;
                        break;
                    }

                // ─── 增益 (180-189) ───
                case EventIds.EXT_BUFF_GAINED:
                    {
                        var parts = param.Split(':');
                        if (parts.Length >= 4)
                        {
                            string buffName = GetBuffName(SafeParseUint(parts[1]));
                            msg = $"机器人{parts[0]} 获得 {buffName} Lv{parts[2]}（{parts[3]}s）";
                        }
                        else if (parts.Length >= 2)
                        {
                            string buffName = GetBuffName(SafeParseUint(parts[1]));
                            msg = $"机器人{parts[0]} 获得 {buffName}";
                        }
                        else
                            msg = "获得增益效果";
                        color = UIColors.HealthGreen;
                        cooldown = SPAM_COOLDOWN;
                        break;
                    }
                case EventIds.EXT_BUFF_EXPIRED:
                    {
                        var parts = param.Split(':');
                        if (parts.Length >= 2)
                        {
                            string buffName = GetBuffName(SafeParseUint(parts[1]));
                            msg = $"机器人{parts[0]} 的 {buffName} 已结束";
                        }
                        else
                            msg = "增益效果结束";
                        color = UIColors.Silver;
                        cooldown = SPAM_COOLDOWN;
                        break;
                    }
                case EventIds.EXT_DEFENSE_ZONE_CAPTURED:
                    {
                        var parts = param.Split(':');
                        msg = parts.Length >= 2
                            ? $"机器人{parts[0]} 占领增益点{parts[1]}"
                            : "增益点被占领";
                        color = UIColors.HealthGreen;
                        break;
                    }
                case EventIds.EXT_DEFENSE_ZONE_LOST:
                    {
                        var parts = param.Split(':');
                        msg = parts.Length >= 2
                            ? $"机器人{parts[0]} 丢失增益点{parts[1]}"
                            : "增益点丢失";
                        color = UIColors.Orange;
                        break;
                    }

                // ─── 经济 (190-199) ───
                case EventIds.EXT_AMMO_EXCHANGED:
                    {
                        var parts = param.Split(':');
                        msg = parts.Length >= 4
                            ? $"机器人{parts[0]} 兑换{parts[1]} ×{parts[2]}（花费{parts[3]}金）"
                            : "弹药兑换";
                        color = UIColors.Silver;
                        cooldown = SPAM_COOLDOWN;
                        break;
                    }
                case EventIds.EXT_REMOTE_HEAL:
                    {
                        var parts = param.Split(':');
                        msg = parts.Length >= 2
                            ? $"机器人{parts[0]} 远程回血（花费{parts[1]}金）"
                            : "远程回血";
                        color = UIColors.HealthGreen;
                        cooldown = SPAM_COOLDOWN;
                        break;
                    }
                case EventIds.EXT_GOLD_INCOME:
                    {
                        var parts = param.Split(':');
                        if (parts.Length >= 3)
                            msg = $"{TeamName(parts[0])}获得金币 +{parts[1]}（{GoldSourceName(parts[2])}）";
                        else if (parts.Length >= 2)
                            msg = $"{TeamName(parts[0])}获得金币 +{parts[1]}";
                        else
                            msg = "金币收入";
                        color = UIColors.HeatYellow;
                        cooldown = SPAM_COOLDOWN;
                        break;
                    }
                case EventIds.EXT_SENTRY_SUPPLY_AMMO:
                    msg = $"哨兵补给弹药 +{param}";
                    color = UIColors.Silver;
                    cooldown = SPAM_COOLDOWN;
                    break;

                // ─── 英雄/哨兵 (200-209) ───
                case EventIds.EXT_HERO_DEPLOY_EXIT:
                    msg = "己方英雄退出部署模式";
                    color = UIColors.Silver;
                    break;
                case EventIds.EXT_SENTRY_POSTURE_CHANGE:
                    {
                        string posture = param switch
                        {
                            "0" => "攻击",
                            "1" => "防御",
                            "2" => "移动",
                            _ => param
                        };
                        msg = $"哨兵切换姿态 → {posture}";
                        color = UIColors.BrightBlue;
                        cooldown = SPAM_COOLDOWN;
                        break;
                    }

                // ─── 雷达 (210-219) ───
                case EventIds.EXT_RADAR_MARK_THRESHOLD:
                    {
                        var parts = param.Split(':');
                        msg = parts.Length >= 2
                            ? $"[!] 雷达标记：目标{parts[0]} 进度{parts[1]}%"
                            : "雷达标记达阈值";
                        color = UIColors.Orange;
                        cooldown = SPAM_COOLDOWN;
                        break;
                    }
                case EventIds.EXT_RADAR_DOUBLE_VULN:
                    msg = "⚡ 雷达双倍易伤触发！";
                    color = UIColors.Red;
                    duration = 3f;
                    break;

                // ─── 等级 (220-229) ───
                case EventIds.EXT_LEVEL_UP:
                    {
                        var parts = param.Split(':');
                        msg = parts.Length >= 2
                            ? $"⬆ 机器人{parts[0]} 升级至 Lv{parts[1]}"
                            : "等级提升";
                        color = UIColors.BrightBlue;
                        break;
                    }
                case EventIds.EXT_MAX_LEVEL_REACHED:
                    {
                        var parts = param.Split(':');
                        msg = parts.Length >= 2
                            ? $"✦ 机器人{parts[0]} 达到等级上限 Lv{parts[1]}"
                            : "达到等级上限";
                        color = UIColors.HeatYellow;
                        break;
                    }

                // ─── 底盘能量 (230-239) ───
                case EventIds.EXT_ENERGY_SAVING_MODE:
                    msg = $"[!] 机器人{param} 底盘进入节能模式";
                    color = UIColors.Orange;
                    cooldown = SPAM_COOLDOWN;
                    break;
                case EventIds.EXT_ENERGY_BOOST_MODE:
                    msg = $"⚡ 机器人{param} 底盘增强模式";
                    color = UIColors.HealthGreen;
                    cooldown = SPAM_COOLDOWN;
                    break;
                case EventIds.EXT_CHASSIS_POWER_CUT:
                    msg = $"[!] 机器人{param} 底盘断电！";
                    color = UIColors.Red;
                    break;

                // ─── 工程特殊 (240-249) ───
                case EventIds.EXT_ENGINEER_DEFENSE_START:
                    msg = "工程前3分钟防御增益生效";
                    color = UIColors.HealthGreen;
                    break;
                case EventIds.EXT_ENGINEER_DEFENSE_END:
                    msg = "工程前3分钟防御增益结束";
                    color = UIColors.Silver;
                    break;

                // ─── 特殊成就 (250-259) ───
                case EventIds.EXT_FIRST_BLOOD:
                    {
                        var parts = param.Split(':');
                        msg = parts.Length >= 2
                            ? $"🩸 首杀！{parts[0]} 击毁 {parts[1]}"
                            : "🩸 首杀！";
                        color = UIColors.Orange;
                        duration = 4f;
                        break;
                    }
                case EventIds.EXT_MULTI_KILL:
                    {
                        var parts = param.Split(':');
                        if (parts.Length >= 2)
                        {
                            string killWord = parts[1] switch
                            {
                                "2" => "双杀",
                                "3" => "三杀",
                                "4" => "四杀",
                                "5" => "五杀",
                                _ => $"{parts[1]}连杀"
                            };
                            msg = $"🔥 机器人{parts[0]} {killWord}！";
                        }
                        else
                            msg = "🔥 多杀！";
                        color = UIColors.Orange;
                        duration = 3f;
                        break;
                    }
                case EventIds.EXT_COMEBACK:
                    msg = $"🔄 {TeamName(param)}逆转！基地血量反超！";
                    color = UIColors.HeatYellow;
                    duration = 3f;
                    break;

                default:
                    return false;
            }

            eventCooldown[id] = cooldown;
            PushNotification(msg, color, duration);
            return true;
        }

        // ═══════════════════════════════════════════════════════════════
        // 辅助方法
        // ═══════════════════════════════════════════════════════════════

        private void PushNotification(string msg, Color color, float duration = -1f)
        {
            if (cachedHUD == null) return;
            if (duration > 0f)
                cachedHUD.Push(msg, color, duration);
            else
                cachedHUD.Push(msg, color);
        }

        private static string TeamName(string param)
        {
            if (string.IsNullOrEmpty(param)) return "";
            return param switch
            {
                "red" => "红方",
                "blue" => "蓝方",
                _ => ""
            };
        }

        private static string DartTargetName(string target)
        {
            return target switch
            {
                "outpost" => "前哨站",
                "base_fixed" => "基地（固定目标）",
                "base_random_f" => "基地（随机固定目标）",
                "base_random_m" => "基地（随机移动目标）",
                "base_end_m" => "基地（末端移动目标）",
                "1" => "前哨站",
                "2" => "基地（固定目标）",
                "3" => "基地（随机固定目标）",
                "4" => "基地（随机移动目标）",
                "5" => "基地（末端移动目标）",
                _ => target
            };
        }

        private static string GoldSourceName(string source)
        {
            return source switch
            {
                "periodic" => "定时收入",
                "kill" => "击杀奖励",
                "assembly" => "科技核心",
                "comeback" => "逆转奖励",
                "objective" => "目标奖励",
                _ => source
            };
        }

        private static string GetBuffName(uint buffType)
        {
            return buffType switch
            {
                BuffTypes.ATTACK => "攻击增益",
                BuffTypes.DEFENSE => "防御增益",
                BuffTypes.HEAT_COOLDOWN => "冷却增益",
                BuffTypes.BUFFER_ENERGY => "缓冲能量增益",
                BuffTypes.HEAL => "回血增益",
                BuffTypes.VULNERABILITY => "易伤",
                BuffTypes.INVINCIBLE => "无敌",
                BuffTypes.WEAK => "虚弱",
                BuffTypes.SUPPLY_HEAL => "补给区回血",
                BuffTypes.TERRAIN_CROSS => "地形跨越",
                BuffTypes.RUNE_SMALL => "小能量机关",
                BuffTypes.RUNE_LARGE_ATK => "大能量机关·攻击",
                BuffTypes.RUNE_LARGE_DEF => "大能量机关·防御",
                BuffTypes.RUNE_LARGE_COOL => "大能量机关·冷却",
                BuffTypes.ENGINEER_EARLY_DEF => "工程前3分钟防御",
                BuffTypes.ASSEMBLY_DEFENSE => "科技核心防御增益",
                BuffTypes.SENTRY_POSTURE => "哨兵姿态增益",
                BuffTypes.HERO_DEPLOY => "英雄部署模式",
                BuffTypes.RADAR_VULN => "雷达易伤",
                BuffTypes.FORTRESS => "堡垒增益",
                _ => $"未知增益({buffType})"
            };
        }

        private static uint SafeParseUint(string s)
        {
            return uint.TryParse(s, out var v) ? v : 0u;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 统一事件 ID 定义
    // 严格对齐 RoboMaster 2026 通信协议 V1.2.0 §2.2.7
    // 官方 1-18 + 仿真扩展 101+，无冲突
    // ═══════════════════════════════════════════════════════════════

    public static class EventIds
    {
        // ─── 官方协议 event_id (1-18) ───

        /// <summary>1 - 击杀事件。param: "killer_id:victim_id"</summary>
        public const int KILL = 1;
        /// <summary>2 - 建筑被摧毁。param: 目标 id（红方前哨站=11, 蓝方前哨站=111）</summary>
        public const int STRUCTURE_DESTROYED = 2;
        /// <summary>3 - 能量机关可激活次数变化。param: 变化后次数</summary>
        public const int RUNE_CHANCE_CHANGE = 3;
        /// <summary>4 - 能量机关可进入激活状态。无参数</summary>
        public const int RUNE_CAN_ACTIVATE = 4;
        /// <summary>5 - 能量机关臂数+平均环数。param: "arms:rings"</summary>
        public const int RUNE_ARM_RESULT = 5;
        /// <summary>6 - 能量机关被激活（含类型）。param: "small"/"large"</summary>
        public const int RUNE_ACTIVATED = 6;
        /// <summary>7 - 己方英雄进入部署模式。无参数</summary>
        public const int HERO_DEPLOY_MODE = 7;
        /// <summary>8 - 己方英雄狙击伤害。param: 累计伤害</summary>
        public const int HERO_SNIPER_OWN = 8;
        /// <summary>9 - 对方英雄狙击伤害。param: 累计伤害</summary>
        public const int HERO_SNIPER_ENEMY = 9;
        /// <summary>10 - 己方呼叫空中支援。无参数</summary>
        public const int AIR_SUPPORT_OWN = 10;
        /// <summary>11 - 己方空中支援被打断。param: 对方剩余打断次数</summary>
        public const int AIR_SUPPORT_OWN_BROKEN = 11;
        /// <summary>12 - 对方呼叫空中支援。无参数</summary>
        public const int AIR_SUPPORT_ENEMY = 12;
        /// <summary>13 - 对方空中支援被打断。param: 己方剩余打断次数</summary>
        public const int AIR_SUPPORT_ENEMY_BROKEN = 13;
        /// <summary>14 - 飞镖命中。param: 1=前哨站,2=基地固定,3=基地随机固定,4=基地随机移动,5=基地末端移动</summary>
        public const int DART_HIT = 14;
        /// <summary>15 - 飞镖闸门开启。param: 1=己方,2=对方</summary>
        public const int DART_GATE = 15;
        /// <summary>16 - 己方基地遭到攻击。无参数（5s冷却）</summary>
        public const int BASE_ATTACKED = 16;
        /// <summary>17 - 前哨站停转。param: 1=己方,2=对方</summary>
        public const int OUTPOST_STOP_ROTATE = 17;
        /// <summary>18 - 基地护甲展开。param: 1=己方,2=对方</summary>
        public const int BASE_ARMOR_OPEN = 18;

        // ─── 仿真扩展 event_id (101+) ───

        // 比赛流程 (101-109)
        public const int EXT_MATCH_START = 101;
        public const int EXT_MATCH_END = 102;
        public const int EXT_STAGE_CHANGE = 103;
        public const int EXT_MATCH_PAUSED = 104;
        public const int EXT_MATCH_RESUMED = 105;

        // 机器人生死 (110-119)
        public const int EXT_ROBOT_RESPAWN = 110;
        public const int EXT_ROBOT_INSTANT_RESPAWN = 111;
        public const int EXT_ROBOT_OFFLINE = 112;
        public const int EXT_ROBOT_RECONNECT = 113;

        // 前哨站/基地 (120-129)
        public const int EXT_OUTPOST_REBUILT = 120;
        public const int EXT_BASE_DESTROYED = 121;

        // 能量机关 (130-139)
        public const int EXT_RUNE_BUFF_EXPIRED = 130;

        // 科技核心 (140-149)
        public const int EXT_TECH_CORE_ASSEMBLING = 140;
        public const int EXT_TECH_CORE_ASSEMBLED = 141;
        public const int EXT_TECH_CORE_FAILED = 142;
        public const int EXT_TECH_CORE_LEVEL_UP = 143;

        // 飞镖扩展 (150-159)
        public const int EXT_DART_LAUNCHED = 150;
        public const int EXT_DART_GATE_CLOSE = 151;
        public const int EXT_DART_SCREEN_BLOCKED = 152;

        // 空中支援扩展 (160-169)
        public const int EXT_AIR_SUPPORT_END = 160;
        public const int EXT_AERIAL_LOCKED = 161;
        public const int EXT_AERIAL_LOCK_RELEASED = 162;

        // 判罚 (170-179)
        public const int EXT_PENALTY_YELLOW = 170;
        public const int EXT_PENALTY_RED = 171;
        public const int EXT_PENALTY_WARNING = 172;

        // 增益 (180-189)
        public const int EXT_BUFF_GAINED = 180;
        public const int EXT_BUFF_EXPIRED = 181;
        public const int EXT_DEFENSE_ZONE_CAPTURED = 182;
        public const int EXT_DEFENSE_ZONE_LOST = 183;

        // 经济 (190-199)
        public const int EXT_AMMO_EXCHANGED = 190;
        public const int EXT_REMOTE_HEAL = 191;
        public const int EXT_GOLD_INCOME = 192;
        public const int EXT_SENTRY_SUPPLY_AMMO = 193;

        // 英雄/哨兵 (200-209)
        public const int EXT_HERO_DEPLOY_EXIT = 200;
        public const int EXT_SENTRY_POSTURE_CHANGE = 201;

        // 雷达 (210-219)
        public const int EXT_RADAR_MARK_THRESHOLD = 210;
        public const int EXT_RADAR_DOUBLE_VULN = 211;

        // 等级 (220-229)
        public const int EXT_LEVEL_UP = 220;
        public const int EXT_MAX_LEVEL_REACHED = 221;

        // 底盘能量 (230-239)
        public const int EXT_ENERGY_SAVING_MODE = 230;
        public const int EXT_ENERGY_BOOST_MODE = 231;
        public const int EXT_CHASSIS_POWER_CUT = 232;

        // 工程特殊 (240-249)
        public const int EXT_ENGINEER_DEFENSE_START = 240;
        public const int EXT_ENGINEER_DEFENSE_END = 241;

        // 特殊成就 (250-259)
        public const int EXT_FIRST_BLOOD = 250;
        public const int EXT_MULTI_KILL = 251;
        public const int EXT_COMEBACK = 252;
    }

    /// <summary>
    /// Buff 类型常量，对应 Buff.buff_type 字段。
    /// </summary>
    public static class BuffTypes
    {
        public const uint ATTACK = 1;
        public const uint DEFENSE = 2;
        public const uint HEAT_COOLDOWN = 3;
        public const uint BUFFER_ENERGY = 4;
        public const uint HEAL = 5;
        public const uint VULNERABILITY = 6;
        public const uint INVINCIBLE = 7;
        public const uint WEAK = 8;
        public const uint SUPPLY_HEAL = 9;
        public const uint TERRAIN_CROSS = 10;
        public const uint RUNE_SMALL = 11;
        public const uint RUNE_LARGE_ATK = 12;
        public const uint RUNE_LARGE_DEF = 13;
        public const uint RUNE_LARGE_COOL = 14;
        public const uint ENGINEER_EARLY_DEF = 15;
        public const uint ASSEMBLY_DEFENSE = 16;
        public const uint SENTRY_POSTURE = 17;
        public const uint HERO_DEPLOY = 18;
        public const uint RADAR_VULN = 19;
        public const uint FORTRESS = 20;
    }
}
