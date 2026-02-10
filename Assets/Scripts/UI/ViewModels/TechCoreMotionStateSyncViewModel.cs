using Google.Protobuf;

namespace UI.ViewModels
{
    public class TechCoreMotionStateSyncViewModel : ProtoViewModelBase<TechCoreMotionStateSync>
    {
        private uint maximumDifficultyLevel;
        private uint status;
        private uint enemyCoreStatus;
        private uint remainTimeAll;
        private uint remainTimeStep;

        public uint MaximumDifficultyLevel { get => maximumDifficultyLevel; set { if (maximumDifficultyLevel != value) { maximumDifficultyLevel = value; OnPropertyChanged(); } } }
        public uint Status { get => status; set { if (status != value) { status = value; OnPropertyChanged(); } } }
        public uint EnemyCoreStatus { get => enemyCoreStatus; set { if (enemyCoreStatus != value) { enemyCoreStatus = value; OnPropertyChanged(); } } }
        public uint RemainTimeAll { get => remainTimeAll; set { if (remainTimeAll != value) { remainTimeAll = value; OnPropertyChanged(); } } }
        public uint RemainTimeStep { get => remainTimeStep; set { if (remainTimeStep != value) { remainTimeStep = value; OnPropertyChanged(); } } }

        protected override void UpdateFrom(TechCoreMotionStateSync msg)
        {
            MaximumDifficultyLevel = msg.MaximumDifficultyLevel;
            Status = msg.Status;
            EnemyCoreStatus = msg.EnemyCoreStatus;
            RemainTimeAll = msg.RemainTimeAll;
            RemainTimeStep = msg.RemainTimeStep;
        }
    }
}
