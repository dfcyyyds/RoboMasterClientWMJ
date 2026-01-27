using TMPro;
using UI.ViewModels;
using UnityEngine;

namespace UI.Views
{
    public class BuffView : ProtoViewBase<BuffViewModel>
    {
        public TMP_Text RobotIdText;
        public TMP_Text BuffTypeText;
        public TMP_Text BuffLevelText;
        public TMP_Text BuffMaxTimeText;
        public TMP_Text BuffLeftTimeText;
        public TMP_Text MsgParamsText;

        protected override BuffViewModel CreateViewModel() => new BuffViewModel();

        protected override void RenderAll()
        {
            var vm = viewModel;
            if (RobotIdText) RobotIdText.text = vm.RobotId.ToString();
            if (BuffTypeText) BuffTypeText.text = vm.BuffType.ToString();
            if (BuffLevelText) BuffLevelText.text = vm.BuffLevel.ToString();
            if (BuffMaxTimeText) BuffMaxTimeText.text = vm.BuffMaxTime.ToString();
            if (BuffLeftTimeText) BuffLeftTimeText.text = vm.BuffLeftTime.ToString();
            if (MsgParamsText) MsgParamsText.text = vm.MsgParams ?? string.Empty;
        }
    }
}
