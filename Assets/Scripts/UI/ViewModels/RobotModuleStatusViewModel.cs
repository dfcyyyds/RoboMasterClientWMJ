using Google.Protobuf;

namespace UI.ViewModels
{
    public class RobotModuleStatusViewModel : ProtoViewModelBase<RobotModuleStatus>
    {
        private uint powerManager;
        private uint rfid;
        private uint lightStrip;
        private uint smallShooter;
        private uint bigShooter;
        private uint uwb;
        private uint armor;
        private uint videoTransmission;
        private uint capacitor;
        private uint mainController;

        public uint PowerManager { get => powerManager; set { if (powerManager != value) { powerManager = value; OnPropertyChanged(); } } }
        public uint Rfid { get => rfid; set { if (rfid != value) { rfid = value; OnPropertyChanged(); } } }
        public uint LightStrip { get => lightStrip; set { if (lightStrip != value) { lightStrip = value; OnPropertyChanged(); } } }
        public uint SmallShooter { get => smallShooter; set { if (smallShooter != value) { smallShooter = value; OnPropertyChanged(); } } }
        public uint BigShooter { get => bigShooter; set { if (bigShooter != value) { bigShooter = value; OnPropertyChanged(); } } }
        public uint Uwb { get => uwb; set { if (uwb != value) { uwb = value; OnPropertyChanged(); } } }
        public uint Armor { get => armor; set { if (armor != value) { armor = value; OnPropertyChanged(); } } }
        public uint VideoTransmission { get => videoTransmission; set { if (videoTransmission != value) { videoTransmission = value; OnPropertyChanged(); } } }
        public uint Capacitor { get => capacitor; set { if (capacitor != value) { capacitor = value; OnPropertyChanged(); } } }
        public uint MainController { get => mainController; set { if (mainController != value) { mainController = value; OnPropertyChanged(); } } }

        protected override void UpdateFrom(RobotModuleStatus msg)
        {
            PowerManager = msg.PowerManager;
            Rfid = msg.Rfid;
            LightStrip = msg.LightStrip;
            SmallShooter = msg.SmallShooter;
            BigShooter = msg.BigShooter;
            Uwb = msg.Uwb;
            Armor = msg.Armor;
            VideoTransmission = msg.VideoTransmission;
            Capacitor = msg.Capacitor;
            MainController = msg.MainController;
        }
    }
}
