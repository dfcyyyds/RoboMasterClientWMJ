using TMPro;
using UI.ViewModels;
using UnityEngine;

namespace UI.Views
{
    public class SentryStatusSyncView : ProtoViewBase<SentryStatusSyncViewModel>
    {
        public TMP_Text PostureIdText;
        public TMP_Text IsWeakenedText;

        protected override SentryStatusSyncViewModel CreateViewModel() => new SentryStatusSyncViewModel();

        protected override void RenderAll()
        {
            var vm = viewModel;
            if (PostureIdText) PostureIdText.text = vm.PostureId.ToString();
            if (IsWeakenedText) IsWeakenedText.text = vm.IsWeakened.ToString();
        }
    }
}
