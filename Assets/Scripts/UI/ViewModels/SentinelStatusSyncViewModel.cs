using Google.Protobuf;

namespace UI.ViewModels
{
    public class SentinelStatusSyncViewModel : ProtoViewModelBase<SentinelStatusSync>
    {
        private uint postureId;
        private bool isWeakened;

        public uint PostureId { get => postureId; set { if (postureId != value) { postureId = value; OnPropertyChanged(); } } }
        public bool IsWeakened { get => isWeakened; set { if (isWeakened != value) { isWeakened = value; OnPropertyChanged(); } } }

        protected override void UpdateFrom(SentinelStatusSync msg)
        {
            PostureId = msg.PostureId;
            IsWeakened = msg.IsWeakened;
        }
    }
}
