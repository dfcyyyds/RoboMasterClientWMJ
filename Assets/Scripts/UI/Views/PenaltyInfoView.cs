using TMPro;
using UI.ViewModels;
using UnityEngine;

namespace UI.Views
{
    public class PenaltyInfoView : ProtoViewBase<PenaltyInfoViewModel>
    {
        public TMP_Text PenaltyTypeText;
        public TMP_Text PenaltyEffectSecText;
        public TMP_Text TotalPenaltyNumText;

        protected override PenaltyInfoViewModel CreateViewModel() => new PenaltyInfoViewModel();

        protected override void RenderAll()
        {
            var vm = viewModel;
            if (PenaltyTypeText) PenaltyTypeText.text = vm.PenaltyType.ToString();
            if (PenaltyEffectSecText) PenaltyEffectSecText.text = vm.PenaltyEffectSec.ToString();
            if (TotalPenaltyNumText) TotalPenaltyNumText.text = vm.TotalPenaltyNum.ToString();
        }
    }
}
