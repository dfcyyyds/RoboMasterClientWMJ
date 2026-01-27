using TMPro;
using UI.ViewModels;
using UnityEngine;

namespace UI.Views
{
    public class GlobalLogisticsStatusView : ProtoViewBase<GlobalLogisticsStatusViewModel>
    {
        public TMP_Text RemainingEconomyText;
        public TMP_Text TotalEconomyObtainedText;
        public TMP_Text TechLevelText;
        public TMP_Text EncryptionLevelText;

        protected override GlobalLogisticsStatusViewModel CreateViewModel() => new GlobalLogisticsStatusViewModel();

        protected override void RenderAll()
        {
            var vm = viewModel;
            if (RemainingEconomyText) RemainingEconomyText.text = vm.RemainingEconomy.ToString();
            if (TotalEconomyObtainedText) TotalEconomyObtainedText.text = vm.TotalEconomyObtained.ToString();
            if (TechLevelText) TechLevelText.text = vm.TechLevel.ToString();
            if (EncryptionLevelText) EncryptionLevelText.text = vm.EncryptionLevel.ToString();
        }
    }
}
