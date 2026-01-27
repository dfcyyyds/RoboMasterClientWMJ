using Google.Protobuf;

namespace UI.ViewModels
{
    public class AirSupportStatusSyncViewModel : ProtoViewModelBase<AirSupportStatusSync>
    {
        private uint airsupportStatus;
        private uint leftTime;
        private uint costCoins;

        public uint AirsupportStatus { get => airsupportStatus; set { if (airsupportStatus != value) { airsupportStatus = value; OnPropertyChanged(); } } }
        public uint LeftTime { get => leftTime; set { if (leftTime != value) { leftTime = value; OnPropertyChanged(); } } }
        public uint CostCoins { get => costCoins; set { if (costCoins != value) { costCoins = value; OnPropertyChanged(); } } }

        protected override void UpdateFrom(AirSupportStatusSync msg)
        {
            AirsupportStatus = msg.AirsupportStatus;
            LeftTime = msg.LeftTime;
            CostCoins = msg.CostCoins;
        }
    }
}
