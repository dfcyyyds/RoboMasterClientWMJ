using Google.Protobuf;

namespace UI.ViewModels
{
    public class GuardCtrlResultViewModel : ProtoViewModelBase<GuardCtrlResult>
    {
        private uint commandId;
        private uint resultCode;

        public uint CommandId { get => commandId; set { if (commandId != value) { commandId = value; OnPropertyChanged(); } } }
        public uint ResultCode { get => resultCode; set { if (resultCode != value) { resultCode = value; OnPropertyChanged(); } } }

        protected override void UpdateFrom(GuardCtrlResult msg)
        {
            CommandId = msg.CommandId;
            ResultCode = msg.ResultCode;
        }
    }
}
