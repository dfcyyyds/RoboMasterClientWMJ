using Google.Protobuf;

namespace UI.ViewModels
{
    /// <summary>
    /// 全局单位状态 ViewModel（V1.2.0: 新增对方基地/前哨站字段，重命名伤害字段）
    /// </summary>
    public class GlobalUnitStatusViewModel : ProtoViewModelBase<GlobalUnitStatus>
    {
        private uint baseHealth;
        private uint baseStatus;
        private uint baseShield;
        private uint outpostHealth;
        private uint outpostStatus;
        private uint enemyBaseHealth;
        private uint enemyBaseStatus;
        private uint enemyBaseShield;
        private uint enemyOutpostHealth;
        private uint enemyOutpostStatus;
        private string robotHealth;
        private string robotBullets;
        private uint totalDamageAlly;
        private uint totalDamageEnemy;

        public uint BaseHealth { get => baseHealth; set { if (baseHealth != value) { baseHealth = value; OnPropertyChanged(); } } }
        public uint BaseStatus { get => baseStatus; set { if (baseStatus != value) { baseStatus = value; OnPropertyChanged(); } } }
        public uint BaseShield { get => baseShield; set { if (baseShield != value) { baseShield = value; OnPropertyChanged(); } } }
        public uint OutpostHealth { get => outpostHealth; set { if (outpostHealth != value) { outpostHealth = value; OnPropertyChanged(); } } }
        public uint OutpostStatus { get => outpostStatus; set { if (outpostStatus != value) { outpostStatus = value; OnPropertyChanged(); } } }
        public uint EnemyBaseHealth { get => enemyBaseHealth; set { if (enemyBaseHealth != value) { enemyBaseHealth = value; OnPropertyChanged(); } } }
        public uint EnemyBaseStatus { get => enemyBaseStatus; set { if (enemyBaseStatus != value) { enemyBaseStatus = value; OnPropertyChanged(); } } }
        public uint EnemyBaseShield { get => enemyBaseShield; set { if (enemyBaseShield != value) { enemyBaseShield = value; OnPropertyChanged(); } } }
        public uint EnemyOutpostHealth { get => enemyOutpostHealth; set { if (enemyOutpostHealth != value) { enemyOutpostHealth = value; OnPropertyChanged(); } } }
        public uint EnemyOutpostStatus { get => enemyOutpostStatus; set { if (enemyOutpostStatus != value) { enemyOutpostStatus = value; OnPropertyChanged(); } } }
        public string RobotHealth { get => robotHealth; set { if (robotHealth != value) { robotHealth = value; OnPropertyChanged(); } } }
        public string RobotBullets { get => robotBullets; set { if (robotBullets != value) { robotBullets = value; OnPropertyChanged(); } } }
        public uint TotalDamageAlly { get => totalDamageAlly; set { if (totalDamageAlly != value) { totalDamageAlly = value; OnPropertyChanged(); } } }
        public uint TotalDamageEnemy { get => totalDamageEnemy; set { if (totalDamageEnemy != value) { totalDamageEnemy = value; OnPropertyChanged(); } } }

        protected override void UpdateFrom(GlobalUnitStatus msg)
        {
            BaseHealth = msg.BaseHealth;
            BaseStatus = msg.BaseStatus;
            BaseShield = msg.BaseShield;
            OutpostHealth = msg.OutpostHealth;
            OutpostStatus = msg.OutpostStatus;
            EnemyBaseHealth = msg.EnemyBaseHealth;
            EnemyBaseStatus = msg.EnemyBaseStatus;
            EnemyBaseShield = msg.EnemyBaseShield;
            EnemyOutpostHealth = msg.EnemyOutpostHealth;
            EnemyOutpostStatus = msg.EnemyOutpostStatus;
            RobotHealth = string.Join(", ", msg.RobotHealth);
            RobotBullets = string.Join(", ", msg.RobotBullets);
            TotalDamageAlly = msg.TotalDamageAlly;
            TotalDamageEnemy = msg.TotalDamageEnemy;
        }
    }
}
