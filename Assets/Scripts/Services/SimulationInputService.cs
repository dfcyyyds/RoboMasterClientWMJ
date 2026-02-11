using UnityEngine;
using Google.Protobuf;
using UI.RobotSelection;

/// <summary>
/// 编辑器仿真按键服务 — 仅在 UNITY_EDITOR 下激活
/// 
/// 功能：
///   - Enter 键单按：发射一颗子弹
///   - Enter 键长按：以武器合理频率连续发射（17mm: 5发/s, 42mm: 2发/s）
///   - 每次射击通过 CommonCommand(cmd_type=100) 发送至 MockServer
///   - 服务器完成热量、弹药、弹道等全部逻辑计算并回传状态
///   
/// 本类不做任何本地数值计算，严格遵循"数据交互驱动"原则。
/// </summary>
public class SimulationInputService : MonoBehaviour
{
#if UNITY_EDITOR
    // ─── 配置 ───
    private float fireInterval = 0.2f; // 17mm 默认 5Hz 射速
    private float fireTimer;
    private bool lastEnterState;
    private bool isHolding;
    private bool initialized;
    private string ammoType = "17mm";

    /// <summary>
    /// 根据兵种初始化射速参数
    /// </summary>
    public void Initialize(RobotType robotType)
    {
        var profile = RobotCapabilities.GetProfile(robotType);
        if (profile == null || !profile.CanShoot)
        {
            enabled = false;
            wmj.Log.I("[SimInput] 当前兵种无射击能力，仿真按键已禁用", wmj.Log.Tag.UI);
            return;
        }

        ammoType = profile.AmmoType ?? "17mm";
        fireInterval = ammoType == "42mm" ? 0.5f : 0.2f;
        initialized = true;
        wmj.Log.I($"[SimInput] 编辑器仿真按键已启用 | 弹种={ammoType} | 射速={1f / fireInterval:F0}发/s",
            wmj.Log.Tag.UI);
    }

    void Update()
    {
        if (!initialized) return;

        bool enterDown = Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter);

        // 单次按下
        if (enterDown && !lastEnterState)
        {
            SendFireCommand();
            fireTimer = fireInterval; // 开始冷却
            isHolding = true;
        }
        // 持续按住 → 连续发射
        else if (enterDown && isHolding)
        {
            fireTimer -= Time.deltaTime;
            if (fireTimer <= 0f)
            {
                SendFireCommand();
                fireTimer = fireInterval;
            }
        }
        // 松开
        else if (!enterDown)
        {
            isHolding = false;
            fireTimer = 0f;
        }

        lastEnterState = enterDown;
    }

    private void SendFireCommand()
    {
        if (NetworkManager.Instance == null)
        {
            wmj.Log.W("[SimInput] NetworkManager 未就绪，无法发送射击指令", wmj.Log.Tag.Network);
            return;
        }

        var cmd = new CommonCommand();
        cmd.CmdType = 100; // 射击指令
        cmd.Param = 0;

        byte[] payload = cmd.ToByteArray();
        NetworkManager.Instance.SendMqttMessage("CommonCommand", payload);
    }
#else
    // 正式构建中完全禁用
    public void Initialize(RobotType robotType) { enabled = false; }
#endif
}
