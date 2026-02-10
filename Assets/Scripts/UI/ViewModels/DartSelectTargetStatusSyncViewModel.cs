using Google.Protobuf;

namespace UI.ViewModels
{
    public class DartSelectTargetStatusSyncViewModel : ProtoViewModelBase<DartSelectTargetStatusSync>
    {
        private uint targetId;
        private uint open;

        public uint TargetId { get => targetId; set { if (targetId != value) { targetId = value; OnPropertyChanged(); } } }
        public uint Open { get => open; set { if (open != value) { open = value; OnPropertyChanged(); } } }

        protected override void UpdateFrom(DartSelectTargetStatusSync msg)
        {
            TargetId = msg.TargetId;
            Open = msg.Open;
        }
    }
}
