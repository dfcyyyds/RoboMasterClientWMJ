using UnityEngine;
using Google.Protobuf;
using UI.Core;
using UI.RobotSelection;
using Framework.Network;

/// <summary>
/// 激战自动补给服务
/// 
/// 功能：
///   - 实时监控弹丸剩余数量
///   - 当处于射击状态（非脱战）且弹药低于阈值时，自动执行弹药购买
///   - 购买通过 CommonCommand(cmd_type=101, param=批次数) 发送至 MockServer
///   - 服务器执行经济扣除、弹药增加并回传状态
///   - 购买数量/阈值可在设置面板中配置
///   
/// 严格遵守官方规则：
///   - 17mm: 每批次 10 金币换 10 发，全队上限 1000 发
///   - 42mm: 每批次 10 金币换 1 发，全队上限 100 发
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
    private const float PURCHASE_INTERVAL = 2.0f; // 每 2 秒最多购买一次

    // ─── 射击检测 ───
    private uint lastProjectilesFired;
    private float recentFireTimer; // 最近一次射击后的计时

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
        wmj.Log.I($"[AutoResupply] 自动补给已就绪 | 弹种={ammoType}", wmj.Log.Tag.UI);
    }

    void Update()
    {
        if (!initialized || !canShoot) return;

        var settings = UILayoutManager.Settings;
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

        // 检测射击活动
        if (currentProjectiles > lastProjectilesFired)
        {
            recentFireTimer = 0f; // 有射击活动
        }
        lastProjectilesFired = currentProjectiles;
        recentFireTimer += Time.deltaTime;

        // 激战判定：最近 3 秒内有射击行为且未脱战
        bool inCombat = recentFireTimer < 3f && !isOutOfCombat;
        if (!inCombat) return;

        // 弹药低于阈值
        if (currentAmmo >= settings.autoResupplyThreshold) return;

        // 购买冷却
        if (purchaseCooldown > 0f) return;

        // 经济检查
        uint costPerBatch = (ammoType == "42mm") ? 10u : 10u;
        uint batchesToBuy = settings.autoResupplyBatchCount;
        uint totalCost = costPerBatch * batchesToBuy;
        if (remainingEconomy < costPerBatch) return; // 至少够买一批

        // 实际可买批数（不超过经济允许的范围）
        uint affordableBatches = remainingEconomy / costPerBatch;
        if (affordableBatches < batchesToBuy)
            batchesToBuy = affordableBatches;

        // 发送购买指令
        SendAmmoPurchaseCommand(batchesToBuy);
        purchaseCooldown = PURCHASE_INTERVAL;

        uint amountPerBatch = (ammoType == "42mm") ? 1u : 10u;
        wmj.Log.I($"[AutoResupply] 自动补给: 购买 {batchesToBuy} 批 = {batchesToBuy * amountPerBatch} 发 {ammoType}" +
                  $" | 当前弹药={currentAmmo} | 阈值={settings.autoResupplyThreshold}",
            wmj.Log.Tag.UI);
    }

    private void SendAmmoPurchaseCommand(uint batches)
    {
        if (NetworkManager.Instance == null) return;

        var cmd = new CommonCommand();
        cmd.CmdType = 101; // 弹药购买指令
        cmd.Param = batches;

        byte[] payload = cmd.ToByteArray();
        NetworkManager.Instance.SendMqttMessage("CommonCommand", payload);
    }
}
