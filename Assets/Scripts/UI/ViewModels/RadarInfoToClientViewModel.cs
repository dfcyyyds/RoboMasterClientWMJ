using Google.Protobuf;

namespace UI.ViewModels
{
    /// <summary>
    /// 雷达信息 ViewModel（原 RaderInfoToClientViewModel，修正拼写）
    /// </summary>
    public class RadarInfoToClientViewModel : ProtoViewModelBase<RadarInfoToClient>
    {
        private uint targetRobotId;
        private float targetPosX;
        private float targetPosY;
        private float torwardAngle;
        private uint isHighLight;

        public uint TargetRobotId { get => targetRobotId; set { if (targetRobotId != value) { targetRobotId = value; OnPropertyChanged(); } } }
        public float TargetPosX { get => targetPosX; set { if (targetPosX != value) { targetPosX = value; OnPropertyChanged(); } } }
        public float TargetPosY { get => targetPosY; set { if (targetPosY != value) { targetPosY = value; OnPropertyChanged(); } } }
        public float TorwardAngle { get => torwardAngle; set { if (torwardAngle != value) { torwardAngle = value; OnPropertyChanged(); } } }
        public uint IsHighLight { get => isHighLight; set { if (isHighLight != value) { isHighLight = value; OnPropertyChanged(); } } }

        protected override void UpdateFrom(RadarInfoToClient msg)
        {
            TargetRobotId = msg.TargetRobotId;
            TargetPosX = msg.TargetPosX;
            TargetPosY = msg.TargetPosY;
            TorwardAngle = msg.TorwardAngle;
            IsHighLight = msg.IsHighLight;
        }
    }
}
