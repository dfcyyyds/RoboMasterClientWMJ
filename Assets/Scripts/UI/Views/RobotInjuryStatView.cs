using TMPro;
using UI.ViewModels;
using UnityEngine;

namespace UI.Views
{
    public class RobotInjuryStatView : ProtoViewBase<RobotInjuryStatViewModel>
    {
        public TMP_Text TotalDamageText;
        public TMP_Text CollisionDamageText;
        public TMP_Text SmallProjectileDamageText;
        public TMP_Text LargeProjectileDamageText;
        public TMP_Text DartSplashDamageText;
        public TMP_Text ModuleOfflineDamageText;
        public TMP_Text WifiOfflineDamageText;
        public TMP_Text PenaltyDamageText;
        public TMP_Text ServerKillDamageText;
        public TMP_Text KillerIdText;

        protected override RobotInjuryStatViewModel CreateViewModel() => new RobotInjuryStatViewModel();

        protected override void RenderAll()
        {
            var vm = viewModel;
            if (TotalDamageText) TotalDamageText.text = vm.TotalDamage.ToString();
            if (CollisionDamageText) CollisionDamageText.text = vm.CollisionDamage.ToString();
            if (SmallProjectileDamageText) SmallProjectileDamageText.text = vm.SmallProjectileDamage.ToString();
            if (LargeProjectileDamageText) LargeProjectileDamageText.text = vm.LargeProjectileDamage.ToString();
            if (DartSplashDamageText) DartSplashDamageText.text = vm.DartSplashDamage.ToString();
            if (ModuleOfflineDamageText) ModuleOfflineDamageText.text = vm.ModuleOfflineDamage.ToString();
            if (WifiOfflineDamageText) WifiOfflineDamageText.text = vm.WifiOfflineDamage.ToString();
            if (PenaltyDamageText) PenaltyDamageText.text = vm.PenaltyDamage.ToString();
            if (ServerKillDamageText) ServerKillDamageText.text = vm.ServerKillDamage.ToString();
            if (KillerIdText) KillerIdText.text = vm.KillerId.ToString();
        }
    }
}
