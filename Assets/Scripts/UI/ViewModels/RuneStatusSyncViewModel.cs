using Google.Protobuf;

namespace UI.ViewModels
{
    public class RuneStatusSyncViewModel : ProtoViewModelBase<RuneStatusSync>
    {
        private uint runeStatus;
        private uint activatedArms;
        private uint averageRings;

        public uint RuneStatus { get => runeStatus; set { if (runeStatus != value) { runeStatus = value; OnPropertyChanged(); } } }
        public uint ActivatedArms { get => activatedArms; set { if (activatedArms != value) { activatedArms = value; OnPropertyChanged(); } } }
        public uint AverageRings { get => averageRings; set { if (averageRings != value) { averageRings = value; OnPropertyChanged(); } } }

        protected override void UpdateFrom(RuneStatusSync msg)
        {
            RuneStatus = msg.RuneStatus;
            ActivatedArms = msg.ActivatedArms;
            AverageRings = msg.AverageRings;
        }
    }
}
