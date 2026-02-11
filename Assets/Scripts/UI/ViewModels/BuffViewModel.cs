using Google.Protobuf;
using UnityEngine;

namespace UI.ViewModels
{
    /// <summary>
    /// Buff ViewModel（V1.2.0: 移除 MsgParams 字段）
    /// </summary>
    public class BuffViewModel : ProtoViewModelBase<Buff>
    {
        private uint robotId;
        private uint buffType;
        private int buffLevel;
        private uint buffMaxTime;
        private uint buffLeftTime;

        // 日志节流：每10秒最多输出一条buff=0日志
        private float lastZeroLogTime;

        public uint RobotId { get => robotId; set { if (robotId != value) { robotId = value; OnPropertyChanged(); } } }
        public uint BuffType { get => buffType; set { if (buffType != value) { buffType = value; OnPropertyChanged(); } } }
        public int BuffLevel { get => buffLevel; set { if (buffLevel != value) { buffLevel = value; OnPropertyChanged(); } } }
        public uint BuffMaxTime { get => buffMaxTime; set { if (buffMaxTime != value) { buffMaxTime = value; OnPropertyChanged(); } } }
        public uint BuffLeftTime { get => buffLeftTime; set { if (buffLeftTime != value) { buffLeftTime = value; OnPropertyChanged(); } } }

        protected override void UpdateFrom(Buff msg)
        {
            // 诊断日志：追踪服务器发送的 buff 原始数据
            if (msg.BuffType != 0)
            {
                Debug.Log($"[BuffVM] 收到BUFF数据: robot={msg.RobotId} type={msg.BuffType} lv={msg.BuffLevel} max={msg.BuffMaxTime} left={msg.BuffLeftTime}");
            }
            else
            {
                float now = Time.realtimeSinceStartup;
                if (now - lastZeroLogTime > 10f)
                {
                    Debug.Log($"[BuffVM] 服务器发送 buff_type=0（当前无BUFF）");
                    lastZeroLogTime = now;
                }
            }

            RobotId = msg.RobotId;
            BuffType = msg.BuffType;
            BuffLevel = msg.BuffLevel;
            BuffMaxTime = msg.BuffMaxTime;
            BuffLeftTime = msg.BuffLeftTime;
        }
    }
}
