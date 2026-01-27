using TMPro;
using UI.ViewModels;
using UnityEngine;

namespace UI.Views
{
    public class RaderInfoToClientView : ProtoViewBase<RaderInfoToClientViewModel>
    {
        public TMP_Text TargetRobotIdText;
        public TMP_Text TargetPosXText;
        public TMP_Text TargetPosYText;
        public TMP_Text TorwardAngleText;
        public TMP_Text IsHighLightText;

        protected override RaderInfoToClientViewModel CreateViewModel() => new RaderInfoToClientViewModel();

        protected override void RenderAll()
        {
            var vm = viewModel;
            if (TargetRobotIdText) TargetRobotIdText.text = vm.TargetRobotId.ToString();
            if (TargetPosXText) TargetPosXText.text = vm.TargetPosX.ToString("F2");
            if (TargetPosYText) TargetPosYText.text = vm.TargetPosY.ToString("F2");
            if (TorwardAngleText) TorwardAngleText.text = vm.TorwardAngle.ToString("F2");
            if (IsHighLightText) IsHighLightText.text = vm.IsHighLight.ToString();
        }
    }
}
