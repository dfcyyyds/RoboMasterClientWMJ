using Google.Protobuf;

namespace UI.ViewModels
{
    public class DeployModeStatusSyncViewModel : ProtoViewModelBase<DeployModeStatusSync>
    {
        private uint status;
        public uint Status { get => status; set { if (status != value) { status = value; OnPropertyChanged(); } } }

        protected override void UpdateFrom(DeployModeStatusSync msg)
        {
            Status = msg.Status;
        }
    }
}
