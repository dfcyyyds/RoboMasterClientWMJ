using Google.Protobuf;

namespace UI.ViewModels
{
    public class GlobalUnitStatusViewModel : ProtoViewModelBase<GlobalUnitStatus>
    {
        private uint baseHealth;
        private uint baseStatus;
        private uint baseShield;
        private uint outpostHealth;
        private uint outpostStatus;
        private string robotHealth;
        private string robotBullets;
        private uint totalDamageRed;
        private uint totalDamageBlue;

        public uint BaseHealth { get => baseHealth; set { if (baseHealth != value) { baseHealth = value; OnPropertyChanged(); } } }
        public uint BaseStatus { get => baseStatus; set { if (baseStatus != value) { baseStatus = value; OnPropertyChanged(); } } }
        public uint BaseShield { get => baseShield; set { if (baseShield != value) { baseShield = value; OnPropertyChanged(); } } }
        public uint OutpostHealth { get => outpostHealth; set { if (outpostHealth != value) { outpostHealth = value; OnPropertyChanged(); } } }
        public uint OutpostStatus { get => outpostStatus; set { if (outpostStatus != value) { outpostStatus = value; OnPropertyChanged(); } } }
        public string RobotHealth { get => robotHealth; set { if (robotHealth != value) { robotHealth = value; OnPropertyChanged(); } } }
        public string RobotBullets { get => robotBullets; set { if (robotBullets != value) { robotBullets = value; OnPropertyChanged(); } } }
        public uint TotalDamageRed { get => totalDamageRed; set { if (totalDamageRed != value) { totalDamageRed = value; OnPropertyChanged(); } } }
        public uint TotalDamageBlue { get => totalDamageBlue; set { if (totalDamageBlue != value) { totalDamageBlue = value; OnPropertyChanged(); } } }

        protected override void UpdateFrom(GlobalUnitStatus msg)
        {
            BaseHealth = msg.BaseHealth;
            BaseStatus = msg.BaseStatus;
            BaseShield = msg.BaseShield;
            OutpostHealth = msg.OutpostHealth;
            OutpostStatus = msg.OutpostStatus;
            RobotHealth = string.Join(", ", msg.RobotHealth);
            RobotBullets = string.Join(", ", msg.RobotBullets);
            TotalDamageRed = msg.TotalDamageRed;
            TotalDamageBlue = msg.TotalDamageBlue;
        }
    }
}
