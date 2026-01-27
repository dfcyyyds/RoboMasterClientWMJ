using Google.Protobuf;
using TMPro;
using UI.ViewModels;
using UnityEngine;

namespace UI.Views
{
    public class GuardCtrlResultView : ProtoViewBase<GuardCtrlResultViewModel>
    {
        public TMP_Text CommandIdText;
        public TMP_Text ResultCodeText;

        protected override GuardCtrlResultViewModel CreateViewModel() => new GuardCtrlResultViewModel();

        protected override void RenderAll()
        {
            var vm = viewModel;
            if (CommandIdText) CommandIdText.text = vm.CommandId.ToString();
            if (ResultCodeText) ResultCodeText.text = vm.ResultCode.ToString();
        }
    }
}
