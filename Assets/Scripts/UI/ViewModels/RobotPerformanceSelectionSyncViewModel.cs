using Google.Protobuf;

namespace UI.ViewModels
{
    public class RobotPerformanceSelectionSyncViewModel : ProtoViewModelBase<RobotPerformanceSelectionSync>
    {
        private uint shooter;
        private uint chassis;

        public uint Shooter { get => shooter; set { if (shooter != value) { shooter = value; OnPropertyChanged(); } } }
        public uint Chassis { get => chassis; set { if (chassis != value) { chassis = value; OnPropertyChanged(); } } }

        protected override void UpdateFrom(RobotPerformanceSelectionSync msg)
        {
            Shooter = msg.Shooter;
            Chassis = msg.Chassis;
        }
    }
}
