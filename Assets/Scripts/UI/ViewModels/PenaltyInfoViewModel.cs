using Google.Protobuf;

namespace UI.ViewModels
{
    public class PenaltyInfoViewModel : ProtoViewModelBase<PenaltyInfo>
    {
        private uint penaltyType;
        private uint penaltyEffectSec;
        private uint totalPenaltyNum;

        public uint PenaltyType { get => penaltyType; set { if (penaltyType != value) { penaltyType = value; OnPropertyChanged(); } } }
        public uint PenaltyEffectSec { get => penaltyEffectSec; set { if (penaltyEffectSec != value) { penaltyEffectSec = value; OnPropertyChanged(); } } }
        public uint TotalPenaltyNum { get => totalPenaltyNum; set { if (totalPenaltyNum != value) { totalPenaltyNum = value; OnPropertyChanged(); } } }

        protected override void UpdateFrom(PenaltyInfo msg)
        {
            PenaltyType = msg.PenaltyType;
            PenaltyEffectSec = msg.PenaltyEffectSec;
            TotalPenaltyNum = msg.TotalPenaltyNum;
        }
    }
}
