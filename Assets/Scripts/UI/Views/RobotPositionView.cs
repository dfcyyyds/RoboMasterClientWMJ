using TMPro;
using UI.ViewModels;
using UnityEngine;

namespace UI.Views
{
    public class RobotPositionView : ProtoViewBase<RobotPositionViewModel>
    {
        public TMP_Text XText;
        public TMP_Text YText;
        public TMP_Text ZText;
        public TMP_Text YawText;

        protected override RobotPositionViewModel CreateViewModel() => new RobotPositionViewModel();

        protected override void RenderAll()
        {
            var vm = viewModel;
            if (XText) XText.text = vm.X.ToString("F2");
            if (YText) YText.text = vm.Y.ToString("F2");
            if (ZText) ZText.text = vm.Z.ToString("F2");
            if (YawText) YawText.text = vm.Yaw.ToString("F2");
        }
    }
}
