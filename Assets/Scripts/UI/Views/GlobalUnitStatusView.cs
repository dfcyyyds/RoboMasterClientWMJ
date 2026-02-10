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
        public TMP_Text EnemyBaseHealthText;
        public TMP_Text EnemyBaseStatusText;
        public TMP_Text EnemyBaseShieldText;
        public TMP_Text EnemyOutpostHealthText;
        public TMP_Text EnemyOutpostStatusText;
        public TMP_Text RobotHealthText;
        public TMP_Text RobotBulletsText;
        public TMP_Text TotalDamageAllyText;
        public TMP_Text TotalDamageEnemyText;

        protected override GlobalUnitStatusViewModel CreateViewModel() => new GlobalUnitStatusViewModel();

        protected override void RenderAll()
        {
            var vm = viewModel;
            if (BaseHealthText) BaseHealthText.text = vm.BaseHealth.ToString();
            if (BaseStatusText) BaseStatusText.text = vm.BaseStatus.ToString();
            if (BaseShieldText) BaseShieldText.text = vm.BaseShield.ToString();
            if (OutpostHealthText) OutpostHealthText.text = vm.OutpostHealth.ToString();
            if (OutpostStatusText) OutpostStatusText.text = vm.OutpostStatus.ToString();
            if (EnemyBaseHealthText) EnemyBaseHealthText.text = vm.EnemyBaseHealth.ToString();
            if (EnemyBaseStatusText) EnemyBaseStatusText.text = vm.EnemyBaseStatus.ToString();
            if (EnemyBaseShieldText) EnemyBaseShieldText.text = vm.EnemyBaseShield.ToString();
            if (EnemyOutpostHealthText) EnemyOutpostHealthText.text = vm.EnemyOutpostHealth.ToString();
            if (EnemyOutpostStatusText) EnemyOutpostStatusText.text = vm.EnemyOutpostStatus.ToString();
            if (RobotHealthText) RobotHealthText.text = vm.RobotHealth;
            if (RobotBulletsText) RobotBulletsText.text = vm.RobotBullets;
            if (TotalDamageAllyText) TotalDamageAllyText.text = vm.TotalDamageAlly.ToString();
            if (TotalDamageEnemyText) TotalDamageEnemyText.text = vm.TotalDamageEnemy.ToString();
        }
    }
}
