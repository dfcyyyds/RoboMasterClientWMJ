using TMPro;
using UI.ViewModels;
using UnityEngine;

namespace UI.Views
{
    public class RobotRespawnStatusView : ProtoViewBase<RobotRespawnStatusViewModel>
    {
        public TMP_Text IsPendingRespawnText;
        public TMP_Text TotalRespawnProgressText;
        public TMP_Text CurrentRespawnProgressText;
        public TMP_Text CanFreeRespawnText;
        public TMP_Text GoldCostForRespawnText;
        public TMP_Text CanPayForRespawnText;

        protected override RobotRespawnStatusViewModel CreateViewModel() => new RobotRespawnStatusViewModel();

        protected override void RenderAll()
        {
            var vm = viewModel;
            if (IsPendingRespawnText) IsPendingRespawnText.text = vm.IsPendingRespawn.ToString();
            if (TotalRespawnProgressText) TotalRespawnProgressText.text = vm.TotalRespawnProgress.ToString();
            if (CurrentRespawnProgressText) CurrentRespawnProgressText.text = vm.CurrentRespawnProgress.ToString();
            if (CanFreeRespawnText) CanFreeRespawnText.text = vm.CanFreeRespawn.ToString();
            if (GoldCostForRespawnText) GoldCostForRespawnText.text = vm.GoldCostForRespawn.ToString();
            if (CanPayForRespawnText) CanPayForRespawnText.text = vm.CanPayForRespawn.ToString();
        }
    }
}
