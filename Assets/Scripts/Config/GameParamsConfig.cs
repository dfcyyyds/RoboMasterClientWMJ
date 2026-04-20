using System;
using System.IO;
using UnityEngine;

/// <summary>
/// 游戏逻辑参数配置 — 从 JSON 文件加载所有可调参数
/// 与 ConfigLoader（服务器连接参数）和 UILayoutManager（UI外观参数）分离
/// 存储于 StreamingAssets/Config/game_params.json
/// 
/// 设计原则：所有需要调节的游戏逻辑参数都集中在此处，不硬编码
/// </summary>
[Serializable]
public class GameParamsData
{
    // ═══════════════════ 运行模式 ═══════════════════

    /// <summary>是否为比赛模式（true=连接官方服务器，使用官方cmd_type；false=仿真MockServer模式）</summary>
    public bool isCompetitionMode = false;
    /// <summary>比赛模式下的服务器IP（协议规定为192.168.12.1）</summary>
    public string competitionServerIp = "192.168.12.1";
    /// <summary>比赛模式下的服务器端口</summary>
    public int competitionServerPort = 3333;

    /// <summary>
    /// MQTT clientId 覆盖值（可选）。留空时按"选手端 ID"自动生成，并按 [0x0165,0165,165,357] 顺序 fallback。
    /// 若官方确认服务器要求特定格式，可在此处直接写入（如 "101", "Hero_Blue"），将跳过所有 fallback。
    /// </summary>
    public string mqttClientIdOverride = "";

    /// <summary>MQTT 用户名（可选，官方若要求鉴权时使用）</summary>
    public string mqttUsername = "";

    /// <summary>MQTT 密码（可选，官方若要求鉴权时使用）</summary>
    public string mqttPassword = "";

    // ═══════════════════ 弹药经济 ═══════════════════

    /// <summary>17mm 弹丸每批次金币花费</summary>
    public int ammo17mmCostPerBatch = 10;
    /// <summary>17mm 弹丸每批次数量</summary>
    public int ammo17mmPerBatch = 10;
    /// <summary>17mm 弹丸全队上限</summary>
    public int ammo17mmTeamLimit = 1000;
    /// <summary>42mm 弹丸每批次金币花费</summary>
    public int ammo42mmCostPerBatch = 10;
    /// <summary>42mm 弹丸每批次数量</summary>
    public int ammo42mmPerBatch = 1;
    /// <summary>42mm 弹丸全队上限</summary>
    public int ammo42mmTeamLimit = 100;
    /// <summary>买活基础金币花费</summary>
    public int buybackBaseCost = 50;

    // ═══════════════════ 自动补给 ═══════════════════

    /// <summary>补给指令最小间隔（秒）</summary>
    public float autoResupplyInterval = 2.0f;
    /// <summary>激战判定窗口（最近N秒内有射击视为激战）</summary>
    public float autoResupplyCombatWindow = 3.0f;
    /// <summary>紧急补给弹药阈值（低于此值立即触发紧急补给）</summary>
    public int autoResupplyEmergencyThreshold = 20;
    /// <summary>紧急补给批次倍率（正常批数×此倍率）</summary>
    public int autoResupplyEmergencyMultiplier = 2;
    /// <summary>买活金币预留（自动补给时保留的最低金币）</summary>
    public int autoResupplyGoldReserve = 100;
    /// <summary>射速统计窗口（秒），用于预测弹丸消耗</summary>
    public float autoResupplyFireRateWindow = 5.0f;
    /// <summary>预测性提前购买时间（秒），根据射速提前补给</summary>
    public float autoResupplyPredictiveLeadTime = 2.0f;

    // ═══════════════════ 性能体系检查 ═══════════════════

    /// <summary>查询服务器性能体系状态的超时时间（秒）</summary>
    public float perfSyncCheckTimeout = 3.0f;
    /// <summary>查询轮询间隔（秒）</summary>
    public float perfSyncCheckInterval = 0.5f;

    // ═══════════════════ 事件通知 ═══════════════════

    /// <summary>同类事件默认冷却（秒）</summary>
    public float eventDefaultCooldown = 0.3f;
    /// <summary>高频事件冷却（秒）</summary>
    public float eventSpamCooldown = 2.0f;
    /// <summary>每帧最多处理事件数</summary>
    public int maxEventsPerFrame = 8;

    // ═══════════════════ BUFF 系统 ═══════════════════

    /// <summary>新 BUFF 闪光时长（秒）</summary>
    public float buffFlashDuration = 0.4f;
    /// <summary>BUFF 条目高度（px）</summary>
    public float buffBarHeight = 72f;
    /// <summary>BUFF 条目间距（px）</summary>
    public float buffBarGap = 5f;

    // ═══════════════════ 战斗参数 ═══════════════════

    /// <summary>敌方基地最大血量</summary>
    public int baseMaxHealth = 5000;
    /// <summary>英雄弹药显示上限</summary>
    public int heroAmmoDisplayMax = 100;
    /// <summary>非英雄弹药显示上限</summary>
    public int normalAmmoDisplayMax = 500;

    // ═══════════════════ 远程操作 ═══════════════════

    /// <summary>远程回血金币花费</summary>
    public int remoteHealCost = 100;
    /// <summary>远程回血冷却（秒）</summary>
    public float remoteHealCooldown = 10f;
    /// <summary>远程弹药补给金币花费</summary>
    public int remoteAmmoCost = 50;

    // ═══════════════════ 吊射图传 ═══════════════════

    /// <summary>吊射图传中继 UDP 源 IP（空=不过滤），仅仿真模式生效</summary>
    public string lobShotUdpIp = "0.0.0.0";
    /// <summary>吊射图传中继 UDP 监听端口（默认 8888），仅仿真模式生效</summary>
    public int lobShotUdpPort = 8888;
    /// <summary>
    /// 收到吊射 CustomByteBlock 数据帧时是否自动弹出 LobShotHUD。
    /// 比赛模式下，MQTT broker 会把己方机器人/队友机器人的 0x0310 CustomByteBlock
    /// 推送到本客户端的 "CustomByteBlock" topic；开启此项后，无需操作手按 Shift 进入
    /// deploy 模式，HUD 也会在首帧到达时自动显示队友吊射画面。
    /// </summary>
    public bool autoShowLobShotOnIncomingFrame = true;
    /// <summary>比赛模式被动观察模式（true=只收不发控制命令，避免与主客户端冲突）</summary>
    public bool competitionPassiveObserverMode = true;
    /// <summary>比赛模式下是否允许本客户端发送 RobotPerformanceSelectionCommand（默认关闭，避免与主客户端冲突）</summary>
    public bool allowCustomPerformanceSelectionCommandInCompetition = false;
    /// <summary>吊射 v2 画面是否拉伸显示（v3.2.1 起默认关闭，1024×512 已是原生采集分辨率）</summary>
    public bool lobShotStretchTo720x1080 = false;
    /// <summary>拉伸显示时是否使用 SR 超分模型（需重训 1024×512 模型后才能启用）</summary>
    public bool lobShotUseSrWhenStretched = false;
}

/// <summary>
/// 游戏参数配置管理器 — 静态加载器
/// 使用方式: GameParamsConfig.Get.ammo17mmCostPerBatch
/// </summary>
public static class GameParamsConfig
{
    private static GameParamsData _data;
    private static string _path;
    private static bool _loaded;

    /// <summary>获取游戏参数（自动懒加载）</summary>
    public static GameParamsData Get
    {
        get
        {
            if (!_loaded) Load();
            return _data;
        }
    }

    /// <summary>实际加载的配置文件路径（用于自检/排查）</summary>
    public static string LoadedPath
    {
        get
        {
            if (!_loaded) Load();
            return _path;
        }
    }

    /// <summary>加载配置文件</summary>
    public static void Load()
    {
        // persistentDataPath = 用户可写的持久化目录（保留用户修改）
        // streamingAssetsPath = 打包进来的默认配置（只读）
        string persistPath = Path.Combine(Application.persistentDataPath, "Config/game_params.json");
        string streamPath = Path.Combine(Application.streamingAssetsPath, "Config/game_params.json");

        // ═══ 加载路径选择策略 ═══
        // 规则：若 StreamingAssets 版本比 persistent 新（打了更新包/修改了默认配置），
        //       则以 StreamingAssets 为准并覆盖 persistent，避免旧副本永久锁死新默认值。
        //       否则优先读取 persistent（保留用户修改）。
        bool persistExists = File.Exists(persistPath);
        bool streamExists = File.Exists(streamPath);

        if (persistExists && streamExists)
        {
            try
            {
                var persistTime = File.GetLastWriteTimeUtc(persistPath);
                var streamTime = File.GetLastWriteTimeUtc(streamPath);
                if (streamTime > persistTime)
                {
                    // StreamingAssets 更新 → 覆盖 persistent
                    wmj.Log.I($"[GameParams] 检测到默认配置已更新 (stream={streamTime:HH:mm:ss} > persist={persistTime:HH:mm:ss})，强制使用新默认值",
                        wmj.Log.Tag.General);
                    try { File.Copy(streamPath, persistPath, true); } catch { /* ignore */ }
                    _path = streamPath; // 本次从 StreamingAssets 读
                }
                else
                {
                    _path = persistPath;
                }
            }
            catch
            {
                _path = persistPath;
            }
        }
        else if (persistExists)
        {
            _path = persistPath;
        }
        else if (streamExists)
        {
            _path = streamPath;
        }
        else
        {
            _path = persistPath; // 不存在时默认写到持久化目录
        }

        if (File.Exists(_path))
        {
            try
            {
                string json = File.ReadAllText(_path);
                // 去除 UTF-8 BOM
                if (!string.IsNullOrEmpty(json) && json[0] == '\uFEFF')
                    json = json.Substring(1);
                // 去除注释
                json = System.Text.RegularExpressions.Regex.Replace(json, @"//.*", "");
                json = System.Text.RegularExpressions.Regex.Replace(json, @"/\*.*?\*/", "",
                    System.Text.RegularExpressions.RegexOptions.Singleline);
                _data = JsonUtility.FromJson<GameParamsData>(json);
                wmj.Log.I($"[GameParams] 已加载游戏参数: {_path}", wmj.Log.Tag.General);
            }
            catch (Exception e)
            {
                wmj.Log.W($"[GameParams] 加载失败，使用默认值: {e.Message}", wmj.Log.Tag.General);
                _data = new GameParamsData();
            }
        }
        else
        {
            _data = new GameParamsData();
            Save(); // 创建默认配置文件
            wmj.Log.I("[GameParams] 已创建默认游戏参数配置", wmj.Log.Tag.General);
        }
        _loaded = true;
    }

    /// <summary>保存配置到文件</summary>
    public static void Save()
    {
        try
        {
            if (_data == null) _data = new GameParamsData();

            // 保存策略：
            // 1. 先尝试写入原路径（编辑器模式下通常是 StreamingAssets）
            // 2. 若原路径只读（打包后的 Linux），回退到 persistentDataPath
            string savePath = _path;
            if (string.IsNullOrEmpty(savePath))
                savePath = Path.Combine(Application.streamingAssetsPath, "Config/game_params.json");

            string dir = Path.GetDirectoryName(savePath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string json = JsonUtility.ToJson(_data, true);

            try
            {
                File.WriteAllText(savePath, json);
                wmj.Log.I($"[GameParams] 参数已保存: {savePath}", wmj.Log.Tag.General);
            }
            catch (System.UnauthorizedAccessException)
            {
                // StreamingAssets 只读，回退到 persistentDataPath
                string fallbackPath = Path.Combine(Application.persistentDataPath, "Config/game_params.json");
                string fallbackDir = Path.GetDirectoryName(fallbackPath);
                if (!Directory.Exists(fallbackDir)) Directory.CreateDirectory(fallbackDir);
                File.WriteAllText(fallbackPath, json);
                _path = fallbackPath; // 后续读写都用此路径
                wmj.Log.I($"[GameParams] StreamingAssets 只读，已保存到: {fallbackPath}", wmj.Log.Tag.General);
            }
        }
        catch (Exception e)
        {
            wmj.Log.E($"[GameParams] 保存失败: {e.Message}", wmj.Log.Tag.General);
        }
    }

    /// <summary>重新加载配置</summary>
    public static void Reload()
    {
        _loaded = false;
        Load();
    }
}
