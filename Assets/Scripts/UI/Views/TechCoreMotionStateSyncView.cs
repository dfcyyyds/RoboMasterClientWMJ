using TMPro;
using UI.ViewModels;
using UnityEngine;

namespace UI.Views
{
    public class TechCoreMotionStateSyncView : ProtoViewBase<TechCoreMotionStateSyncViewModel>
    {
        public TMP_Text MaximumDifficultyLevelText;
        public TMP_Text BasicStateText;
        public TMP_Text PutinStateText;
        public TMP_Text MoveStateText;
        public TMP_Text RotateStateText;

        protected override TechCoreMotionStateSyncViewModel CreateViewModel() => new TechCoreMotionStateSyncViewModel();

        protected override void RenderAll()
        {
            var vm = viewModel;
            if (MaximumDifficultyLevelText) MaximumDifficultyLevelText.text = vm.MaximumDifficultyLevel.ToString();
            if (BasicStateText) BasicStateText.text = vm.BasicState.ToString();
            if (PutinStateText) PutinStateText.text = vm.PutinState.ToString();
            if (MoveStateText) MoveStateText.text = vm.MoveState.ToString();
            if (RotateStateText) RotateStateText.text = vm.RotateState.ToString();
        }
    }
}
