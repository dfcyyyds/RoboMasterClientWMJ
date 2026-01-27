using TMPro;
using UI.ViewModels;
using UnityEngine;

namespace UI.Views
{
    public class RobotModuleStatusView : ProtoViewBase<RobotModuleStatusViewModel>
    {
        public TMP_Text PowerManagerText;
        public TMP_Text RfidText;
        public TMP_Text LightStripText;
        public TMP_Text SmallShooterText;
        public TMP_Text BigShooterText;
        public TMP_Text UwbText;
        public TMP_Text ArmorText;
        public TMP_Text VideoTransmissionText;
        public TMP_Text CapacitorText;
        public TMP_Text MainControllerText;

        protected override RobotModuleStatusViewModel CreateViewModel() => new RobotModuleStatusViewModel();

        protected override void RenderAll()
        {
            var vm = viewModel;
            if (PowerManagerText) PowerManagerText.text = vm.PowerManager.ToString();
            if (RfidText) RfidText.text = vm.Rfid.ToString();
            if (LightStripText) LightStripText.text = vm.LightStrip.ToString();
            if (SmallShooterText) SmallShooterText.text = vm.SmallShooter.ToString();
            if (BigShooterText) BigShooterText.text = vm.BigShooter.ToString();
            if (UwbText) UwbText.text = vm.Uwb.ToString();
            if (ArmorText) ArmorText.text = vm.Armor.ToString();
            if (VideoTransmissionText) VideoTransmissionText.text = vm.VideoTransmission.ToString();
            if (CapacitorText) CapacitorText.text = vm.Capacitor.ToString();
            if (MainControllerText) MainControllerText.text = vm.MainController.ToString();
        }
    }
}
