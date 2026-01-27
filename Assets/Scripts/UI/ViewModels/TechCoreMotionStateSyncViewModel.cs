using Google.Protobuf;

namespace UI.ViewModels
{
    public class TechCoreMotionStateSyncViewModel : ProtoViewModelBase<TechCoreMotionStateSync>
    {
        private uint maximumDifficultyLevel;
        private uint status;

        public uint MaximumDifficultyLevel { get => maximumDifficultyLevel; set { if (maximumDifficultyLevel != value) { maximumDifficultyLevel = value; OnPropertyChanged(); } } }
        public uint Status { get => status; set { if (status != value) { status = value; OnPropertyChanged(); } } }

        protected override void UpdateFrom(TechCoreMotionStateSync msg)
        {
            MaximumDifficultyLevel = msg.MaximumDifficultyLevel;
            Status = msg.Status;
        }
    }
}
