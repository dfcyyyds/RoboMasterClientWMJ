using Google.Protobuf;

namespace UI.ViewModels
{
    public class DartSelectTargetStatusSyncViewModel : ProtoViewModelBase<DartSelectTargetStatusSync>
    {
        private uint targetId;
        private bool open;

        public uint TargetId { get => targetId; set { if (targetId != value) { targetId = value; OnPropertyChanged(); } } }
        public bool Open { get => open; set { if (open != value) { open = value; OnPropertyChanged(); } } }

        protected override void UpdateFrom(DartSelectTargetStatusSync msg)
        {
            TargetId = msg.TargetId;
            Open = msg.Open;
        }
    }
}
