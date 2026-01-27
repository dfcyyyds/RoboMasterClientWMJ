using TMPro;
using UI.ViewModels;
using UnityEngine;

namespace UI.Views
{
    public class GlobalUnitStatusView : ProtoViewBase<GlobalUnitStatusViewModel>
    {
        public TMP_Text BaseHealthText;
        public TMP_Text BaseStatusText;
        public TMP_Text BaseShieldText;
        public TMP_Text OutpostHealthText;
        public TMP_Text OutpostStatusText;
        public TMP_Text RobotHealthText;
        public TMP_Text RobotBulletsText;
        public TMP_Text TotalDamageRedText;
        public TMP_Text TotalDamageBlueText;

        protected override GlobalUnitStatusViewModel CreateViewModel() => new GlobalUnitStatusViewModel();

        protected override void RenderAll()
        {
            var vm = viewModel;
            if (BaseHealthText) BaseHealthText.text = vm.BaseHealth.ToString();
            if (BaseStatusText) BaseStatusText.text = vm.BaseStatus.ToString();
            if (BaseShieldText) BaseShieldText.text = vm.BaseShield.ToString();
            if (OutpostHealthText) OutpostHealthText.text = vm.OutpostHealth.ToString();
            if (OutpostStatusText) OutpostStatusText.text = vm.OutpostStatus.ToString();
            if (RobotHealthText) RobotHealthText.text = vm.RobotHealth;
            if (RobotBulletsText) RobotBulletsText.text = vm.RobotBullets;
            if (TotalDamageRedText) TotalDamageRedText.text = vm.TotalDamageRed.ToString();
            if (TotalDamageBlueText) TotalDamageBlueText.text = vm.TotalDamageBlue.ToString();
        }
    }
}
