using UnityEngine;
using Google.Protobuf;
using Framework.Network;

/// <summary>
/// 键鼠控制指令发送服务 — 比赛模式下使用
/// 
/// 官方协议要求:
///   - 通过 KeyboardMouseControl 消息将操作手的键鼠输入以 75Hz 发送给机器人
///   - 方向: 自定义客户端 → 图传链路 → 机器人
///   - 字段: mouse_x/y/z (速度), left/right/mid_button_down, keyboard_value (16位掩码)
/// 
/// 键盘位掩码 (bit 0-15):
///   0=W, 1=S, 2=A, 3=D, 4=Shift, 5=Ctrl, 6=Q, 7=E,
///   8=R, 9=F, 10=G, 11=Z, 12=X, 13=C, 14=V, 15=B
/// 
/// 仅在 isCompetitionMode=true 时激活
/// </summary>
public class KeyboardMouseInputService : MonoBehaviour
{
    public static KeyboardMouseInputService Instance { get; private set; }

    private bool isActive;
    private float sendInterval;
    private float sendTimer;

    // 键码映射表
    private static readonly KeyCode[] MappedKeys = {
        KeyCode.W, KeyCode.S, KeyCode.A, KeyCode.D,
        KeyCode.LeftShift, KeyCode.LeftControl,
        KeyCode.Q, KeyCode.E, KeyCode.R, KeyCode.F,
        KeyCode.G, KeyCode.Z, KeyCode.X, KeyCode.C,
        KeyCode.V, KeyCode.B
    };

    void Awake()
    {
        if (Instance != null) { Destroy(this); return; }
        Instance = this;
    }

    /// <summary>初始化并启动键鼠输入发送</summary>
    public void Initialize()
    {
        if (!GameParamsConfig.Get.isCompetitionMode)
        {
            enabled = false;
            wmj.Log.I("[KeyMouseInput] 仿真模式，键鼠服务未启用", wmj.Log.Tag.UI);
            return;
        }

        sendInterval = 1f / 75f; // 75Hz
        sendTimer = 0f;
        isActive = true;
        wmj.Log.I("[KeyMouseInput] 比赛模式键鼠控制服务已启动", wmj.Log.Tag.UI);
    }

    void Update()
    {
        if (!isActive) return;
        if (NetworkManager.Instance == null) return;

        sendTimer -= Time.unscaledDeltaTime;
        if (sendTimer > 0f) return;
        sendTimer = sendInterval;

        // ─── 采集鼠标 ───
        // Unity 的 Input.GetAxis 返回帧间移动量（像素），乘以系数转为速度
        float mouseSpeedScale = 10f;
        int mouseX = Mathf.RoundToInt(Input.GetAxisRaw("Mouse X") * mouseSpeedScale);
        int mouseY = Mathf.RoundToInt(Input.GetAxisRaw("Mouse Y") * mouseSpeedScale);
        int mouseZ = Mathf.RoundToInt(Input.GetAxisRaw("Mouse ScrollWheel") * 120f);

        bool leftDown = Input.GetMouseButton(0);
        bool rightDown = Input.GetMouseButton(1);
        bool midDown = Input.GetMouseButton(2);

        // ─── 采集键盘位掩码 ───
        uint keyMask = 0u;
        for (int i = 0; i < MappedKeys.Length; i++)
        {
            if (Input.GetKey(MappedKeys[i]))
                keyMask |= (1u << i);
        }

        // ─── 构造消息 ───
        var msg = new KeyboardMouseControl
        {
            MouseX = mouseX,
            MouseY = mouseY,
            MouseZ = mouseZ,
            LeftButtonDown = leftDown,
            RightButtonDown = rightDown,
            KeyboardValue = keyMask,
            MidButtonDown = midDown,
        };

        byte[] payload = msg.ToByteArray();
        NetworkManager.Instance.SendMqttMessage("KeyboardMouseControl", payload);
    }

    /// <summary>停止发送</summary>
    public void Shutdown()
    {
        isActive = false;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
