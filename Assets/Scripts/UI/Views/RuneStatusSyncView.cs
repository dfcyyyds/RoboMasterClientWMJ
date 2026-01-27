using TMPro;
using UI.ViewModels;
using UnityEngine;

namespace UI.Views
{
    public class RuneStatusSyncView : ProtoViewBase<RuneStatusSyncViewModel>
    {
        public TMP_Text RuneStatusText;
        public TMP_Text ActivatedArmsText;
        public TMP_Text AverageRingsText;

        protected override RuneStatusSyncViewModel CreateViewModel() => new RuneStatusSyncViewModel();

        protected override void RenderAll()
        {
            var vm = viewModel;
            if (RuneStatusText) RuneStatusText.text = vm.RuneStatus.ToString();
            if (ActivatedArmsText) ActivatedArmsText.text = vm.ActivatedArms.ToString();
            if (AverageRingsText) AverageRingsText.text = vm.AverageRings.ToString();
        }
    }
}
