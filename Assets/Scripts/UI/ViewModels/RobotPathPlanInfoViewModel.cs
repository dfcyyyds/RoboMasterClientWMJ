using Google.Protobuf;
using System.Linq;

namespace UI.ViewModels
{
    public class RobotPathPlanInfoViewModel : ProtoViewModelBase<RobotPathPlanInfo>
    {
        private uint intention;
        private uint startPosX;
        private uint startPosY;
        private string offsetX;
        private string offsetY;
        private uint senderId;

        public uint Intention { get => intention; set { if (intention != value) { intention = value; OnPropertyChanged(); } } }
        public uint StartPosX { get => startPosX; set { if (startPosX != value) { startPosX = value; OnPropertyChanged(); } } }
        public uint StartPosY { get => startPosY; set { if (startPosY != value) { startPosY = value; OnPropertyChanged(); } } }
        public string OffsetX { get => offsetX; set { if (offsetX != value) { offsetX = value; OnPropertyChanged(); } } }
        public string OffsetY { get => offsetY; set { if (offsetY != value) { offsetY = value; OnPropertyChanged(); } } }
        public uint SenderId { get => senderId; set { if (senderId != value) { senderId = value; OnPropertyChanged(); } } }

        protected override void UpdateFrom(RobotPathPlanInfo msg)
        {
            Intention = msg.Intention;
            StartPosX = msg.StartPosX;
            StartPosY = msg.StartPosY;
            OffsetX = string.Join(", ", msg.OffsetX.Select(v => v.ToString()));
            OffsetY = string.Join(", ", msg.OffsetY.Select(v => v.ToString()));
            SenderId = msg.SenderId;
        }
    }
}
