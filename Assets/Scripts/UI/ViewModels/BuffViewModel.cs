using Google.Protobuf;

namespace UI.ViewModels
{
    public class BuffViewModel : ProtoViewModelBase<Buff>
    {
        private uint robotId;
        private uint buffType;
        private int buffLevel;
        private uint buffMaxTime;
        private uint buffLeftTime;
        private string msgParams;

        public uint RobotId { get => robotId; set { if (robotId != value) { robotId = value; OnPropertyChanged(); } } }
        public uint BuffType { get => buffType; set { if (buffType != value) { buffType = value; OnPropertyChanged(); } } }
        public int BuffLevel { get => buffLevel; set { if (buffLevel != value) { buffLevel = value; OnPropertyChanged(); } } }
        public uint BuffMaxTime { get => buffMaxTime; set { if (buffMaxTime != value) { buffMaxTime = value; OnPropertyChanged(); } } }
        public uint BuffLeftTime { get => buffLeftTime; set { if (buffLeftTime != value) { buffLeftTime = value; OnPropertyChanged(); } } }
        public string MsgParams { get => msgParams; set { if (msgParams != value) { msgParams = value; OnPropertyChanged(); } } }

        protected override void UpdateFrom(Buff msg)
        {
            RobotId = msg.RobotId;
            BuffType = msg.BuffType;
            BuffLevel = msg.BuffLevel;
            BuffMaxTime = msg.BuffMaxTime;
            BuffLeftTime = msg.BuffLeftTime;
            MsgParams = msg.MsgParams;
        }
    }
}
