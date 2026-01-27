using TMPro;
using UI.ViewModels;
using UnityEngine;

namespace UI.Views
{
    public class DeployModeStatusSyncView : ProtoViewBase<DeployModeStatusSyncViewModel>
    {
        public TMP_Text StatusText;

        protected override DeployModeStatusSyncViewModel CreateViewModel() => new DeployModeStatusSyncViewModel();

        protected override void RenderAll()
        {
            var vm = viewModel;
            if (StatusText) StatusText.text = vm.Status.ToString();
        }
    }
}
