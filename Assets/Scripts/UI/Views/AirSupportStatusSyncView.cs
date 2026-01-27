using TMPro;
using UI.ViewModels;
using UnityEngine;

namespace UI.Views
{
    public class AirSupportStatusSyncView : ProtoViewBase<AirSupportStatusSyncViewModel>
    {
        public TMP_Text AirsupportStatusText;
        public TMP_Text LeftTimeText;
        public TMP_Text CostCoinsText;

        protected override AirSupportStatusSyncViewModel CreateViewModel() => new AirSupportStatusSyncViewModel();

        protected override void RenderAll()
        {
            var vm = viewModel;
            if (AirsupportStatusText) AirsupportStatusText.text = vm.AirsupportStatus.ToString();
            if (LeftTimeText) LeftTimeText.text = vm.LeftTime.ToString();
            if (CostCoinsText) CostCoinsText.text = vm.CostCoins.ToString();
        }
    }
}
