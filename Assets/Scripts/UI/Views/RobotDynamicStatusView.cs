using TMPro;
using UI.ViewModels;
using UnityEngine;

namespace UI.Views
{
    public class RobotDynamicStatusView : ProtoViewBase<RobotDynamicStatusViewModel>
    {
        public TMP_Text CurrentHealthText;
        public TMP_Text CurrentHeatText;
        public TMP_Text LastProjectileFireRateText;
        public TMP_Text CurrentChassisEnergyText;
        public TMP_Text CurrentBufferEnergyText;
        public TMP_Text CurrentExperienceText;
        public TMP_Text ExperienceForUpgradeText;
        public TMP_Text TotalProjectilesFiredText;
        public TMP_Text RemainingAmmoText;
        public TMP_Text IsOutOfCombatText;
        public TMP_Text OutOfCombatCountdownText;
        public TMP_Text CanRemoteHealText;
        public TMP_Text CanRemoteAmmoText;

        protected override RobotDynamicStatusViewModel CreateViewModel() => new RobotDynamicStatusViewModel();

        protected override void RenderAll()
        {
            var vm = viewModel;
            if (CurrentHealthText) CurrentHealthText.text = vm.CurrentHealth.ToString();
            if (CurrentHeatText) CurrentHeatText.text = vm.CurrentHeat.ToString("F2");
            if (LastProjectileFireRateText) LastProjectileFireRateText.text = vm.LastProjectileFireRate.ToString("F2");
            if (CurrentChassisEnergyText) CurrentChassisEnergyText.text = vm.CurrentChassisEnergy.ToString();
            if (CurrentBufferEnergyText) CurrentBufferEnergyText.text = vm.CurrentBufferEnergy.ToString();
            if (CurrentExperienceText) CurrentExperienceText.text = vm.CurrentExperience.ToString();
            if (ExperienceForUpgradeText) ExperienceForUpgradeText.text = vm.ExperienceForUpgrade.ToString();
            if (TotalProjectilesFiredText) TotalProjectilesFiredText.text = vm.TotalProjectilesFired.ToString();
            if (RemainingAmmoText) RemainingAmmoText.text = vm.RemainingAmmo.ToString();
            if (IsOutOfCombatText) IsOutOfCombatText.text = vm.IsOutOfCombat ? "Yes" : "No";
            if (OutOfCombatCountdownText) OutOfCombatCountdownText.text = vm.OutOfCombatCountdown.ToString();
            if (CanRemoteHealText) CanRemoteHealText.text = vm.CanRemoteHeal ? "Yes" : "No";
            if (CanRemoteAmmoText) CanRemoteAmmoText.text = vm.CanRemoteAmmo ? "Yes" : "No";
        }
    }
}
