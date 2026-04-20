using UnityEngine;
using Google.Protobuf;
using UI.Core;
using UI.RobotSelection;
using Framework.Network;

/// <summary>
/// 激战智能补给服务
/// 
/// 功能：
///   - 实时监控弹丸剩余数量
///   - 常规补给：处于射击状态且弹药低于阈值时自动购买
///   - 紧急补给：弹药极低时立即触发，不等激战判定，批次加倍
///   - 金币预留：自动补给时保留买活所需的最低金币
///   - 战斗强度追踪：根据射速动态调整补给量
///   - 预测性补给：根据当前消耗速率提前购买
///   - 所有参数均可在设置面板和 game_params.json 中自定义
///   
/// 严格遵守官方规则（参数来自 game_params.json）：
///   - 17mm: 默认每批次 10 金币换 10 发，全队上限 1000 发
///   - 42mm: 默认每批次 10 金币换 1 发，全队上限 100 发
///   - 不足金币时不触发购买
/// </summary>
public class AutoResupplyService : MonoBehaviour
{
    // ─── 状态 ───
    private bool initialized;
    private bool canShoot;
    private string ammoType = "17mm";

    // ─── 购买冷却 ───
    private float purchaseCooldown;

    // ─── 射击检测 ───
    private uint lastProjectilesFired;
    private float recentFireTimer; // 最近一次射击后的计时

    // ─── 战斗强度追踪 ───
    private float[] shotTimestamps = new float[64]; // 环形缓冲记录射击时间
    private int shotTimestampIdx;
    private int shotTimestampCount;
    private uint shotsInWindow; // 统计窗口内的射弹数
    private float windowResetTimer;

    /// <summary>
    /// 根据兵种初始化
    /// </summary>
    public void Initialize(RobotType robotType)
    {
        var profile = RobotCapabilities.GetProfile(robotType);
        canShoot = profile != null && profile.CanShoot;
        if (!canShoot)
        {
            enabled = false;
            return;
        }

        ammoType = profile.AmmoType ?? "17mm";
        initialized = true;
        wmj.Log.I($"[AutoResupply] 智能补给已就绪 | 弹种={ammoType}", wmj.Log.Tag.UI);
    }

    void Update()
    {
        if (!initialized || !canShoot) return;

        var settings = UILayoutManager.Settings;
        var gp = GameParamsConfig.Get;

        if (!settings.autoResupplyEnabled) return;

        purchaseCooldown -= Time.deltaTime;

        // 从 ProtobufManager 获取最新数据
        var pm = ProtobufManager.Instance;
        var dynamicData = pm.RobotDynamicStatus;
        var logisticsData = pm.GlobalLogisticsStatus;
        if (dynamicData == null || logisticsData == null) return;

        uint currentAmmo = dynamicData.RemainingAmmo;
        uint currentProjectiles = dynamicData.TotalProjectilesFired;
        uint remainingEconomy = logisticsData.RemainingEconomy;
        bool isOutOfCombat = dynamicData.IsOutOfCombat;

        // ─── 射击活动检测 ───
        if (currentProjectiles > lastProjectilesFired)
        {
            uint shotsFired = currentProjectiles - lastProjectilesFired;
            recentFireTimer = 0f;
            RecordShots(shotsFired);
        }
        lastProjectilesFired = currentProjectiles;
        recentFireTimer += Time.deltaTime;

        // ─── 弹药经济参数 ───
        bool isHero = ammoType == "42mm";
        uint costPerBatch = (uint)(isHero ? gp.ammo42mmCostPerBatch : gp.ammo17mmCostPerBatch);
        uint amountPerBatch = (uint)(isHero ? gp.ammo42mmPerBatch : gp.ammo17mmPerBatch);

        // ─── 金币预留计算（保留买活最低金币） ───
        uint goldReserve = settings.smartResupplyEnabled
            ? settings.goldReserveForBuyback
            : 0u;
        uint usableGold = remainingEconomy > goldReserve
            ? remainingEconomy - goldReserve
            : 0u;

        // ═══ 紧急补给逻辑 ═══
        bool isEmergency = currentAmmo <= settings.emergencyAmmoThreshold;
        if (isEmergency && purchaseCooldown <= 0f && usableGold >= costPerBatch)
        {
            // 紧急模式：不等激战判定，立即购买，批次加倍
            uint emergencyBatches = settings.autoResupplyBatchCount
                * (uint)gp.autoResupplyEmergencyMultiplier;
            uint affordableBatches = usableGold / costPerBatch;
            uint actualBatches = System.Math.Min(emergencyBatches, affordableBatches);

            if (actualBatches > 0)
            {
                SendAmmoPurchaseCommand(actualBatches);
                purchaseCooldown = gp.autoResupplyInterval * 0.5f; // 紧急时缩短冷却

                wmj.Log.I($"[AutoResupply] [紧急] 补给: {actualBatches}批={actualBatches * amountPerBatch}发 " +
                    $"{ammoType} | 弹药={currentAmmo} | 可用金币={usableGold}",
                    wmj.Log.Tag.UI);
            }
            return;
        }

        // ═══ 常规补给逻辑 ═══
        // 激战判定：最近 N 秒内有射击行为且未脱战
        bool inCombat = recentFireTimer < gp.autoResupplyCombatWindow && !isOutOfCombat;
        if (!inCombat) return;
        if (purchaseCooldown > 0f) return;
        if (usableGold < costPerBatch) return;

        // ─── 智能补给量计算 ───
        uint batchesToBuy = settings.autoResupplyBatchCount;

        if (settings.smartResupplyEnabled)
        {
            // 根据战斗强度调整
            float intensity = GetCombatIntensity(gp.autoResupplyFireRateWindow);
            if (intensity > 1.0f)
                batchesToBuy = (uint)(batchesToBuy * Mathf.Min(intensity * settings.combatIntensityWeight, 5f));

            // 预测性补给：根据消耗速率计算未来N秒需要的弹药
            float fireRate = GetRecentFireRate(gp.autoResupplyFireRateWindow);
            if (fireRate > 0f && currentAmmo <= settings.autoResupplyThreshold)
            {
                float predictedNeed = fireRate * gp.autoResupplyPredictiveLeadTime;
                uint predictedBatches = amountPerBatch > 0
                    ? (uint)Mathf.CeilToInt(predictedNeed / amountPerBatch)
                    : batchesToBuy;
                batchesToBuy = System.Math.Max(batchesToBuy, predictedBatches);
            }
        }

        // 常规阈值检查
        if (currentAmmo >= settings.autoResupplyThreshold) return;

        // 金币上限检查
        uint normalAffordable = usableGold / costPerBatch;
        if (normalAffordable < batchesToBuy)
            batchesToBuy = normalAffordable;

        if (batchesToBuy > 0)
        {
            SendAmmoPurchaseCommand(batchesToBuy);
            purchaseCooldown = gp.autoResupplyInterval;

            wmj.Log.I($"[AutoResupply] 常规补给: {batchesToBuy}批={batchesToBuy * amountPerBatch}发 " +
                $"{ammoType} | 弹药={currentAmmo} | 阈值={settings.autoResupplyThreshold} | 可用金币={usableGold}",
                wmj.Log.Tag.UI);
        }
    }

    // ─── 射速追踪 ───

    /// <summary>记录射击事件到环形缓冲区</summary>
    private void RecordShots(uint count)
    {
        float now = Time.time;
        for (uint i = 0; i < count && i < 16; i++) // 限制单帧记录
        {
            shotTimestamps[shotTimestampIdx] = now;
            shotTimestampIdx = (shotTimestampIdx + 1) % shotTimestamps.Length;
            if (shotTimestampCount < shotTimestamps.Length)
                shotTimestampCount++;
        }
        shotsInWindow += count;
    }

    /// <summary>获取最近N秒内的射速（发/秒）</summary>
    private float GetRecentFireRate(float windowSec)
    {
        if (shotTimestampCount == 0) return 0f;

        float now = Time.time;
        float cutoff = now - windowSec;
        int count = 0;

        for (int i = 0; i < shotTimestampCount; i++)
        {
            int idx = (shotTimestampIdx - 1 - i + shotTimestamps.Length) % shotTimestamps.Length;
            if (shotTimestamps[idx] >= cutoff)
                count++;
            else
                break;
        }

        return windowSec > 0f ? count / windowSec : 0f;
    }

    /// <summary>获取战斗强度（基于射速的归一化值，1.0=平均，>1.0=激战）</summary>
    private float GetCombatIntensity(float windowSec)
    {
        float rate = GetRecentFireRate(windowSec);
        // 基准射速：17mm 约 10发/秒，42mm 约 2发/秒
        float baseRate = ammoType == "42mm" ? 2f : 10f;
        return baseRate > 0f ? rate / baseRate : 0f;
    }

    private void SendAmmoPurchaseCommand(uint batches)
    {
        if (NetworkManager.Instance == null) return;

        var gp = GameParamsConfig.Get;
        if (gp.isCompetitionMode)
        {
            // 官方协议：cmd_type=1(17mm), cmd_type=2(42mm)
            // 17mm: param 必须为 10 的倍数（表示发数）
            // 42mm: param 表示发数
            bool isHeroAmmo = ammoType == "42mm";
            var cmd = new CommonCommand();
            cmd.CmdType = isHeroAmmo ? 2u : 1u;
            uint amountPerBatch = (uint)(isHeroAmmo ? gp.ammo42mmPerBatch : gp.ammo17mmPerBatch);
            cmd.Param = batches * amountPerBatch; // 官方要求发数，非批次数

            byte[] payload = cmd.ToByteArray();
            NetworkManager.Instance.SendMqttMessage("CommonCommand", payload);
        }
        else
        {
            // 仿真 MockServer 模式
            var cmd = new CommonCommand();
            cmd.CmdType = 101; // MockServer 弹药购买指令
            cmd.Param = batches;

            byte[] payload = cmd.ToByteArray();
            NetworkManager.Instance.SendMqttMessage("CommonCommand", payload);
        }
    }
}
