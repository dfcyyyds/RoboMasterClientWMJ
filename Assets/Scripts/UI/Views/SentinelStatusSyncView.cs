using TMPro;
using UI.ViewModels;
using UnityEngine;

namespace UI.Views
{
    public class SentinelStatusSyncView : ProtoViewBase<SentinelStatusSyncViewModel>
    {
        public TMP_Text PostureIdText;
        public TMP_Text IsWeakenedText;

        protected override SentinelStatusSyncViewModel CreateViewModel() => new SentinelStatusSyncViewModel();

        protected override void RenderAll()
        {
            var vm = viewModel;
            if (PostureIdText) PostureIdText.text = vm.PostureId.ToString();
            if (IsWeakenedText) IsWeakenedText.text = vm.IsWeakened.ToString();
        }
    }
}
