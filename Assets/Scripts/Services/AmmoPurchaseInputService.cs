using UnityEngine;
using Google.Protobuf;
using UI.Core;
using UI.RobotSelection;
using Framework.Network;
using System.Collections.Generic;

namespace UI.HUD
{
    /// <summary>
    /// 弹药快捷购买服务 — 数字键快速购买弹丸
    /// 
    /// 功能：
    ///   - 检测设置中配置的快捷键（默认: 1~0 = 数字1~10）
    ///   - 非英雄单位：购买 digit × 10 发弹丸（如按"1" = 10发, "5" = 50发）
    ///   - 英雄单位：购买 digit × 1 发弹丸（如按"1" = 1发, "5" = 5发）
    ///   - 购买成功后在画面中央显示六边形提示弹窗
    ///   - 快捷键可在设置面板"快捷键"栏目中自定义
    ///   
    /// 仅在对局进行中且机器人存活时可用。
    /// </summary>
    public class AmmoPurchaseInputService : MonoBehaviour
    {
        // ─── 配置 ───
        private bool initialized;
        private bool canShoot;
        private bool isHero;
        private string ammoType = "17mm";

        // ─── 冷却 ───
        private float purchaseCooldown;
        private const float PURCHASE_COOLDOWN = 0.3f;

        // ─── 关联 ───
        private HexPopupHUD hexPopup;

        // ─── 弹药常量（与服务器一致） ───
        private const uint AMMO_17MM_PER_BATCH = 10;  // 每批 10 发
        private const uint AMMO_42MM_PER_BATCH = 1;   // 每批 1 发
        private const uint COST_PER_BATCH = 10;       // 每批 10 金币

        /// <summary>
        /// 初始化服务
        /// </summary>
        public void Initialize(RobotType robotType, HexPopupHUD popup)
        {
            var profile = RobotCapabilities.GetProfile(robotType);
            canShoot = profile != null && profile.CanShoot;
            if (!canShoot)
            {
                enabled = false;
                wmj.Log.I("[AmmoPurchase] 当前兵种无射击能力，弹药购买已禁用", wmj.Log.Tag.UI);
                return;
            }

            isHero = (robotType == RobotType.Hero);
            ammoType = profile.AmmoType ?? "17mm";
            hexPopup = popup;
            initialized = true;

            wmj.Log.I($"[AmmoPurchase] 弹药快捷购买已就绪 | 弹种={ammoType} | 英雄={isHero}",
                wmj.Log.Tag.UI);
        }

        void Update()
        {
            if (!initialized || !canShoot) return;

            purchaseCooldown -= Time.deltaTime;
            if (purchaseCooldown > 0f) return;

            // 检查机器人是否存活
            var pm = ProtobufManager.Instance;
            if (pm == null) return;
            var respawnData = pm.RobotRespawnStatus;
            if (respawnData != null && respawnData.IsPendingRespawn) return; // 死亡中不能购买

            var settings = UILayoutManager.Settings;
            if (settings.ammoKeyBindings == null || settings.ammoKeyBindings.Count == 0) return;

            // 检测快捷键
            foreach (var binding in settings.ammoKeyBindings)
            {
                KeyCode key = (KeyCode)binding.keyCode;
                if (Input.GetKeyDown(key))
                {
                    int digit = binding.purchaseDigit;
                    ExecutePurchase(digit);
                    break;
                }
            }
        }

        /// <summary>
        /// 执行弹药购买
        /// </summary>
        /// <param name="digit">购买数字（非英雄: digit×10 发, 英雄: digit×1 发）</param>
        private void ExecutePurchase(int digit)
        {
            if (digit <= 0) return;
            if (NetworkManager.Instance == null) return;

            // 计算需要的批次数
            // 非英雄(17mm): 每批10发, 要买 digit×10 发 → digit 批
            // 英雄(42mm): 每批1发, 要买 digit×1 发 → digit 批
            uint batchCount = (uint)digit;
            uint ammoPerBatch = isHero ? AMMO_42MM_PER_BATCH : AMMO_17MM_PER_BATCH;
            uint totalAmmo = batchCount * ammoPerBatch;
            uint totalCost = batchCount * COST_PER_BATCH;

            // 客户端经济预检查
            var logisticsData = ProtobufManager.Instance?.GlobalLogisticsStatus;
            if (logisticsData != null)
            {
                uint remainingEconomy = logisticsData.RemainingEconomy;
                if (remainingEconomy < COST_PER_BATCH)
                {
                    wmj.Log.W($"[AmmoPurchase] 金币不足，无法购买弹药 | 剩余={remainingEconomy}",
                        wmj.Log.Tag.UI);
                    return;
                }
            }

            // 发送购买指令：CommonCommand(cmd_type=101, param=批次数)
            var cmd = new CommonCommand();
            cmd.CmdType = 101;
            cmd.Param = batchCount;

            byte[] payload = cmd.ToByteArray();
            NetworkManager.Instance.SendMqttMessage("CommonCommand", payload);

            purchaseCooldown = PURCHASE_COOLDOWN;

            // 显示六边形购买提示
            if (hexPopup != null)
            {
                hexPopup.ShowPurchase((int)totalAmmo, ammoType, totalCost);
            }

            wmj.Log.I($"[AmmoPurchase] 快捷购买: {digit}键 → {totalAmmo}发 {ammoType} | 花费≈{totalCost}金",
                wmj.Log.Tag.UI);
        }
    }
}
