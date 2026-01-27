using TMPro;
using UI.ViewModels;
using UnityEngine;

namespace UI.Views
{
    public class RobotPerformanceSelectionSyncView : ProtoViewBase<RobotPerformanceSelectionSyncViewModel>
    {
        public TMP_Text ShooterText;
        public TMP_Text ChassisText;

        protected override RobotPerformanceSelectionSyncViewModel CreateViewModel() => new RobotPerformanceSelectionSyncViewModel();

        protected override void RenderAll()
        {
            var vm = viewModel;
            if (ShooterText) ShooterText.text = vm.Shooter.ToString();
            if (ChassisText) ChassisText.text = vm.Chassis.ToString();
        }
    }
}
