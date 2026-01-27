using Google.Protobuf;

namespace UI.ViewModels
{
    public class RobotRespawnStatusViewModel : ProtoViewModelBase<RobotRespawnStatus>
    {
        private bool isPendingRespawn;
        private uint totalRespawnProgress;
        private uint currentRespawnProgress;
        private bool canFreeRespawn;
        private uint goldCostForRespawn;
        private bool canPayForRespawn;

        public bool IsPendingRespawn { get => isPendingRespawn; set { if (isPendingRespawn != value) { isPendingRespawn = value; OnPropertyChanged(); } } }
        public uint TotalRespawnProgress { get => totalRespawnProgress; set { if (totalRespawnProgress != value) { totalRespawnProgress = value; OnPropertyChanged(); } } }
        public uint CurrentRespawnProgress { get => currentRespawnProgress; set { if (currentRespawnProgress != value) { currentRespawnProgress = value; OnPropertyChanged(); } } }
        public bool CanFreeRespawn { get => canFreeRespawn; set { if (canFreeRespawn != value) { canFreeRespawn = value; OnPropertyChanged(); } } }
        public uint GoldCostForRespawn { get => goldCostForRespawn; set { if (goldCostForRespawn != value) { goldCostForRespawn = value; OnPropertyChanged(); } } }
        public bool CanPayForRespawn { get => canPayForRespawn; set { if (canPayForRespawn != value) { canPayForRespawn = value; OnPropertyChanged(); } } }

        protected override void UpdateFrom(RobotRespawnStatus msg)
        {
            IsPendingRespawn = msg.IsPendingRespawn;
            TotalRespawnProgress = msg.TotalRespawnProgress;
            CurrentRespawnProgress = msg.CurrentRespawnProgress;
            CanFreeRespawn = msg.CanFreeRespawn;
            GoldCostForRespawn = msg.GoldCostForRespawn;
            CanPayForRespawn = msg.CanPayForRespawn;
        }
    }
}
