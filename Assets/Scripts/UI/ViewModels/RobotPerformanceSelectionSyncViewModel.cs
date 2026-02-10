using Google.Protobuf;

namespace UI.ViewModels
{
    public class RobotPerformanceSelectionSyncViewModel : ProtoViewModelBase<RobotPerformanceSelectionSync>
    {
        private uint shooter;
        private uint chassis;
        private uint sentryControl;

        public uint Shooter { get => shooter; set { if (shooter != value) { shooter = value; OnPropertyChanged(); } } }
        public uint Chassis { get => chassis; set { if (chassis != value) { chassis = value; OnPropertyChanged(); } } }
        public uint SentryControl { get => sentryControl; set { if (sentryControl != value) { sentryControl = value; OnPropertyChanged(); } } }

        protected override void UpdateFrom(RobotPerformanceSelectionSync msg)
        {
            Shooter = msg.Shooter;
            Chassis = msg.Chassis;
            SentryControl = msg.SentryControl;
        }
    }
}
