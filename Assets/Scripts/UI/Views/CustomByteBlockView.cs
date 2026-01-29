using System;
using TMPro;
using UI.ViewModels;
using UnityEngine;

namespace UI.Views
{
    public class CustomByteBlockView : ProtoViewBase<CustomByteBlockViewModel>
    {
        public TMP_Text DataText;

        protected override CustomByteBlockViewModel CreateViewModel() => new CustomByteBlockViewModel();

        protected override void RenderAll()
        {
            var vm = viewModel;
            if (DataText)
            {
                // var bytes = vm.Data?.ToByteArray() ?? Array.Empty<byte>();
                // DataText.text = BitConverter.ToString(bytes);
            }
        }
    }
}
