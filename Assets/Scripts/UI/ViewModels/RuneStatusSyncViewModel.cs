using Google.Protobuf;

namespace UI.ViewModels
{
    public class RuneStatusSyncViewModel : ProtoViewModelBase<RuneStatusSync>
    {
        private uint runeStatus;
        private uint activatedArms;
        private float averageRings;

        public uint RuneStatus { get => runeStatus; set { if (runeStatus != value) { runeStatus = value; OnPropertyChanged(); } } }
        public uint ActivatedArms { get => activatedArms; set { if (activatedArms != value) { activatedArms = value; OnPropertyChanged(); } } }
        /// <summary>V1.3.0: 从 uint32(总环数) 改为 float(平均环数)，仅大能量机关正在激活时有效</summary>
        public float AverageRings { get => averageRings; set { if (averageRings != value) { averageRings = value; OnPropertyChanged(); } } }

        protected override void UpdateFrom(RuneStatusSync msg)
        {
            RuneStatus = msg.RuneStatus;
            ActivatedArms = msg.ActivatedArms;
            AverageRings = msg.AverageRings;
        }
    }
}
