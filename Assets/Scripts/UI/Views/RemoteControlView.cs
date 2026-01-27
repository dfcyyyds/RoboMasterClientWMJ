using System;
using TMPro;
using UI.ViewModels;
using UnityEngine;

namespace UI.Views
{
    public class RemoteControlView : ProtoViewBase<RemoteControlViewModel>
    {
        public TMP_Text MouseXText;
        public TMP_Text MouseYText;
        public TMP_Text MouseZText;
        public TMP_Text LeftButtonDownText;
        public TMP_Text RightButtonDownText;
        public TMP_Text KeyboardValueText;
        public TMP_Text MidButtonDownText;
        public TMP_Text DataText;

        protected override RemoteControlViewModel CreateViewModel() => new RemoteControlViewModel();

        protected override void RenderAll()
        {
            var vm = viewModel;
            if (MouseXText) MouseXText.text = vm.MouseX.ToString();
            if (MouseYText) MouseYText.text = vm.MouseY.ToString();
            if (MouseZText) MouseZText.text = vm.MouseZ.ToString();
            if (LeftButtonDownText) LeftButtonDownText.text = vm.LeftButtonDown.ToString();
            if (RightButtonDownText) RightButtonDownText.text = vm.RightButtonDown.ToString();
            if (KeyboardValueText) KeyboardValueText.text = vm.KeyboardValue.ToString();
            if (MidButtonDownText) MidButtonDownText.text = vm.MidButtonDown.ToString();
            if (DataText)
            {
                var bytes = vm.Data?.ToByteArray() ?? Array.Empty<byte>();
                DataText.text = BitConverter.ToString(bytes);
            }
        }
    }
}
