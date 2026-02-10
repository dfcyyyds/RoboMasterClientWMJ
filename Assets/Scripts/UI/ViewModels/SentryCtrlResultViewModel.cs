using Google.Protobuf;

namespace UI.ViewModels
{
    /// <summary>
    /// 哨兵控制结果 ViewModel（原 GuardCtrlResultViewModel）
    /// </summary>
    public class SentryCtrlResultViewModel : ProtoViewModelBase<SentryCtrlResult>
    {
        private uint commandId;
        private uint resultCode;

        public uint CommandId { get => commandId; set { if (commandId != value) { commandId = value; OnPropertyChanged(); } } }
        public uint ResultCode { get => resultCode; set { if (resultCode != value) { resultCode = value; OnPropertyChanged(); } } }

        protected override void UpdateFrom(SentryCtrlResult msg)
        {
            CommandId = msg.CommandId;
            ResultCode = msg.ResultCode;
        }
    }
}
