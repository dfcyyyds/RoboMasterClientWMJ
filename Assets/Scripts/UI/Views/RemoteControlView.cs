using System;
using TMPro;
using UI.ViewModels;
using UnityEngine;

namespace UI.Views
{
    /// <summary>
    /// 键鼠控制视图（原 RemoteControlView，V1.2.0 拆分后去除 Data 字段）
    /// </summary>
    public class KeyboardMouseControlView : ProtoViewBase<KeyboardMouseControlViewModel>
    {
        public TMP_Text MouseXText;
        public TMP_Text MouseYText;
        public TMP_Text MouseZText;
        public TMP_Text LeftButtonDownText;
        public TMP_Text RightButtonDownText;
        public TMP_Text KeyboardValueText;
        public TMP_Text MidButtonDownText;

        protected override KeyboardMouseControlViewModel CreateViewModel() => new KeyboardMouseControlViewModel();

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
        }
    }
}
