using Google.Protobuf;

namespace UI.ViewModels
{
    public class RobotInjuryStatViewModel : ProtoViewModelBase<RobotInjuryStat>
    {
        private uint totalDamage;
        private uint collisionDamage;
        private uint smallProjectileDamage;
        private uint largeProjectileDamage;
        private uint dartSplashDamage;
        private uint moduleOfflineDamage;
        private uint wifiOfflineDamage;
        private uint penaltyDamage;
        private uint serverKillDamage;
        private uint killerId;

        public uint TotalDamage { get => totalDamage; set { if (totalDamage != value) { totalDamage = value; OnPropertyChanged(); } } }
        public uint CollisionDamage { get => collisionDamage; set { if (collisionDamage != value) { collisionDamage = value; OnPropertyChanged(); } } }
        public uint SmallProjectileDamage { get => smallProjectileDamage; set { if (smallProjectileDamage != value) { smallProjectileDamage = value; OnPropertyChanged(); } } }
        public uint LargeProjectileDamage { get => largeProjectileDamage; set { if (largeProjectileDamage != value) { largeProjectileDamage = value; OnPropertyChanged(); } } }
        public uint DartSplashDamage { get => dartSplashDamage; set { if (dartSplashDamage != value) { dartSplashDamage = value; OnPropertyChanged(); } } }
        public uint ModuleOfflineDamage { get => moduleOfflineDamage; set { if (moduleOfflineDamage != value) { moduleOfflineDamage = value; OnPropertyChanged(); } } }
        public uint WifiOfflineDamage { get => wifiOfflineDamage; set { if (wifiOfflineDamage != value) { wifiOfflineDamage = value; OnPropertyChanged(); } } }
        public uint PenaltyDamage { get => penaltyDamage; set { if (penaltyDamage != value) { penaltyDamage = value; OnPropertyChanged(); } } }
        public uint ServerKillDamage { get => serverKillDamage; set { if (serverKillDamage != value) { serverKillDamage = value; OnPropertyChanged(); } } }
        public uint KillerId { get => killerId; set { if (killerId != value) { killerId = value; OnPropertyChanged(); } } }

        protected override void UpdateFrom(RobotInjuryStat msg)
        {
            TotalDamage = msg.TotalDamage;
            CollisionDamage = msg.CollisionDamage;
            SmallProjectileDamage = msg.SmallProjectileDamage;
            LargeProjectileDamage = msg.LargeProjectileDamage;
            DartSplashDamage = msg.DartSplashDamage;
            ModuleOfflineDamage = msg.ModuleOfflineDamage;
            WifiOfflineDamage = msg.WifiOfflineDamage;
            PenaltyDamage = msg.PenaltyDamage;
            ServerKillDamage = msg.ServerKillDamage;
            KillerId = msg.KillerId;
        }
    }
}
