using TMPro;
using UI.ViewModels;
using UnityEngine;

namespace UI.Views
{
    public class RobotStaticStatusView : ProtoViewBase<RobotStaticStatusViewModel>
    {
        public TMP_Text ConnectionStateText;
        public TMP_Text FieldStateText;
        public TMP_Text AliveStateText;
        public TMP_Text RobotIdText;
        public TMP_Text RobotTypeText;
        public TMP_Text PerformanceSystemShooterText;
        public TMP_Text PerformanceSystemChassisText;
        public TMP_Text LevelText;
        public TMP_Text MaxHealthText;
        public TMP_Text MaxHeatText;
        public TMP_Text HeatCooldownRateText;
        public TMP_Text MaxPowerText;
        public TMP_Text MaxBufferEnergyText;
        public TMP_Text MaxChassisEnergyText;

        protected override RobotStaticStatusViewModel CreateViewModel() => new RobotStaticStatusViewModel();

        protected override void RenderAll()
        {
            var vm = viewModel;
            if (ConnectionStateText) ConnectionStateText.text = vm.ConnectionState.ToString();
            if (FieldStateText) FieldStateText.text = vm.FieldState.ToString();
            if (AliveStateText) AliveStateText.text = vm.AliveState.ToString();
            if (RobotIdText) RobotIdText.text = vm.RobotId.ToString();
            if (RobotTypeText) RobotTypeText.text = vm.RobotType.ToString();
            if (PerformanceSystemShooterText) PerformanceSystemShooterText.text = vm.PerformanceSystemShooter.ToString();
            if (PerformanceSystemChassisText) PerformanceSystemChassisText.text = vm.PerformanceSystemChassis.ToString();
            if (LevelText) LevelText.text = vm.Level.ToString();
            if (MaxHealthText) MaxHealthText.text = vm.MaxHealth.ToString();
            if (MaxHeatText) MaxHeatText.text = vm.MaxHeat.ToString();
            if (HeatCooldownRateText) HeatCooldownRateText.text = vm.HeatCooldownRate.ToString("F2");
            if (MaxPowerText) MaxPowerText.text = vm.MaxPower.ToString();
            if (MaxBufferEnergyText) MaxBufferEnergyText.text = vm.MaxBufferEnergy.ToString();
            if (MaxChassisEnergyText) MaxChassisEnergyText.text = vm.MaxChassisEnergy.ToString();
        }
    }
}
