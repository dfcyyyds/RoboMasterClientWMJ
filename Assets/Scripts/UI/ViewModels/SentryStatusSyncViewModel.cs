using Google.Protobuf;

namespace UI.ViewModels
{
    /// <summary>
    /// 哨兵状态同步 ViewModel（原 SentinelStatusSyncViewModel）
    /// </summary>
    public class SentryStatusSyncViewModel : ProtoViewModelBase<SentryStatusSync>
    {
        private uint postureId;
        private bool isWeakened;

        public uint PostureId { get => postureId; set { if (postureId != value) { postureId = value; OnPropertyChanged(); } } }
        public bool IsWeakened { get => isWeakened; set { if (isWeakened != value) { isWeakened = value; OnPropertyChanged(); } } }

        protected override void UpdateFrom(SentryStatusSync msg)
        {
            PostureId = msg.PostureId;
            IsWeakened = msg.IsWeakened;
        }
    }
}
