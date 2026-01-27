using Google.Protobuf;

namespace UI.ViewModels
{
    public class GlobalLogisticsStatusViewModel : ProtoViewModelBase<GlobalLogisticsStatus>
    {
        private uint remainingEconomy;
        private ulong totalEconomyObtained;
        private uint techLevel;
        private uint encryptionLevel;

        public uint RemainingEconomy { get => remainingEconomy; set { if (remainingEconomy != value) { remainingEconomy = value; OnPropertyChanged(); } } }
        public ulong TotalEconomyObtained { get => totalEconomyObtained; set { if (totalEconomyObtained != value) { totalEconomyObtained = value; OnPropertyChanged(); } } }
        public uint TechLevel { get => techLevel; set { if (techLevel != value) { techLevel = value; OnPropertyChanged(); } } }
        public uint EncryptionLevel { get => encryptionLevel; set { if (encryptionLevel != value) { encryptionLevel = value; OnPropertyChanged(); } } }

        protected override void UpdateFrom(GlobalLogisticsStatus msg)
        {
            RemainingEconomy = msg.RemainingEconomy;
            TotalEconomyObtained = msg.TotalEconomyObtained;
            TechLevel = msg.TechLevel;
            EncryptionLevel = msg.EncryptionLevel;
        }
    }
}
