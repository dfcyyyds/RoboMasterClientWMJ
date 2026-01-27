using TMPro;
using UI.ViewModels;
using UnityEngine;

namespace UI.Views
{
    public class TechCoreMotionStateSyncView : ProtoViewBase<TechCoreMotionStateSyncViewModel>
    {
        public TMP_Text MaximumDifficultyLevelText;
        public TMP_Text StatusText;

        protected override TechCoreMotionStateSyncViewModel CreateViewModel() => new TechCoreMotionStateSyncViewModel();

        protected override void RenderAll()
        {
            var vm = viewModel;
            if (MaximumDifficultyLevelText) MaximumDifficultyLevelText.text = vm.MaximumDifficultyLevel.ToString();
            if (StatusText) StatusText.text = vm.Status.ToString();
        }
    }
}
