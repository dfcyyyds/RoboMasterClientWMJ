using TMPro;
using UI.ViewModels;
using UnityEngine;

namespace UI.Views
{
    public class DartSelectTargetStatusSyncView : ProtoViewBase<DartSelectTargetStatusSyncViewModel>
    {
        public TMP_Text TargetIdText;
        public TMP_Text OpenText;

        protected override DartSelectTargetStatusSyncViewModel CreateViewModel() => new DartSelectTargetStatusSyncViewModel();

        protected override void RenderAll()
        {
            var vm = viewModel;
            if (TargetIdText) TargetIdText.text = vm.TargetId.ToString();
            if (OpenText) OpenText.text = vm.Open.ToString();
        }
    }
}
