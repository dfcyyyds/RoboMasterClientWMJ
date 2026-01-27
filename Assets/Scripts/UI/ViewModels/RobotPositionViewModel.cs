using Google.Protobuf;

namespace UI.ViewModels
{
    public class RobotPositionViewModel : ProtoViewModelBase<RobotPosition>
    {
        private float x;
        private float y;
        private float z;
        private float yaw;

        public float X { get => x; set { if (x != value) { x = value; OnPropertyChanged(); } } }
        public float Y { get => y; set { if (y != value) { y = value; OnPropertyChanged(); } } }
        public float Z { get => z; set { if (z != value) { z = value; OnPropertyChanged(); } } }
        public float Yaw { get => yaw; set { if (yaw != value) { yaw = value; OnPropertyChanged(); } } }

        protected override void UpdateFrom(RobotPosition msg)
        {
            X = msg.X;
            Y = msg.Y;
            Z = msg.Z;
            Yaw = msg.Yaw;
        }
    }
}
