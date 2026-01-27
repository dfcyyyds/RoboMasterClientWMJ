using Google.Protobuf;

namespace UI.ViewModels
{
    public class RobotStaticStatusViewModel : ProtoViewModelBase<RobotStaticStatus>
    {
        private uint connectionState;
        private uint fieldState;
        private uint aliveState;
        private uint robotId;
        private uint robotType;
        private uint performanceSystemShooter;
        private uint performanceSystemChassis;
        private uint level;
        private uint maxHealth;
        private uint maxHeat;
        private float heatCooldownRate;
        private uint maxPower;
        private uint maxBufferEnergy;
        private uint maxChassisEnergy;

        public uint ConnectionState { get => connectionState; set { if (connectionState != value) { connectionState = value; OnPropertyChanged(); } } }
        public uint FieldState { get => fieldState; set { if (fieldState != value) { fieldState = value; OnPropertyChanged(); } } }
        public uint AliveState { get => aliveState; set { if (aliveState != value) { aliveState = value; OnPropertyChanged(); } } }
        public uint RobotId { get => robotId; set { if (robotId != value) { robotId = value; OnPropertyChanged(); } } }
        public uint RobotType { get => robotType; set { if (robotType != value) { robotType = value; OnPropertyChanged(); } } }
        public uint PerformanceSystemShooter { get => performanceSystemShooter; set { if (performanceSystemShooter != value) { performanceSystemShooter = value; OnPropertyChanged(); } } }
        public uint PerformanceSystemChassis { get => performanceSystemChassis; set { if (performanceSystemChassis != value) { performanceSystemChassis = value; OnPropertyChanged(); } } }
        public uint Level { get => level; set { if (level != value) { level = value; OnPropertyChanged(); } } }
        public uint MaxHealth { get => maxHealth; set { if (maxHealth != value) { maxHealth = value; OnPropertyChanged(); } } }
        public uint MaxHeat { get => maxHeat; set { if (maxHeat != value) { maxHeat = value; OnPropertyChanged(); } } }
        public float HeatCooldownRate { get => heatCooldownRate; set { if (heatCooldownRate != value) { heatCooldownRate = value; OnPropertyChanged(); } } }
        public uint MaxPower { get => maxPower; set { if (maxPower != value) { maxPower = value; OnPropertyChanged(); } } }
        public uint MaxBufferEnergy { get => maxBufferEnergy; set { if (maxBufferEnergy != value) { maxBufferEnergy = value; OnPropertyChanged(); } } }
        public uint MaxChassisEnergy { get => maxChassisEnergy; set { if (maxChassisEnergy != value) { maxChassisEnergy = value; OnPropertyChanged(); } } }

        protected override void UpdateFrom(RobotStaticStatus msg)
        {
            ConnectionState = msg.ConnectionState;
            FieldState = msg.FieldState;
            AliveState = msg.AliveState;
            RobotId = msg.RobotId;
            RobotType = msg.RobotType;
            PerformanceSystemShooter = msg.PerformanceSystemShooter;
            PerformanceSystemChassis = msg.PerformanceSystemChassis;
            Level = msg.Level;
            MaxHealth = msg.MaxHealth;
            MaxHeat = msg.MaxHeat;
            HeatCooldownRate = msg.HeatCooldownRate;
            MaxPower = msg.MaxPower;
            MaxBufferEnergy = msg.MaxBufferEnergy;
            MaxChassisEnergy = msg.MaxChassisEnergy;
        }
    }
}
