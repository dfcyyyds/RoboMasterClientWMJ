using TMPro;
using UI.ViewModels;
using UnityEngine;

namespace UI.Views
{
    public class RobotPathPlanInfoView : ProtoViewBase<RobotPathPlanInfoViewModel>
    {
        public TMP_Text IntentionText;
        public TMP_Text StartPosXText;
        public TMP_Text StartPosYText;
        public TMP_Text OffsetXText;
        public TMP_Text OffsetYText;
        public TMP_Text SenderIdText;

        protected override RobotPathPlanInfoViewModel CreateViewModel() => new RobotPathPlanInfoViewModel();

        protected override void RenderAll()
        {
            var vm = viewModel;
            if (IntentionText) IntentionText.text = vm.Intention.ToString();
            if (StartPosXText) StartPosXText.text = vm.StartPosX.ToString();
            if (StartPosYText) StartPosYText.text = vm.StartPosY.ToString();
            if (OffsetXText) OffsetXText.text = vm.OffsetX;
            if (OffsetYText) OffsetYText.text = vm.OffsetY;
            if (SenderIdText) SenderIdText.text = vm.SenderId.ToString();
        }
    }
}
