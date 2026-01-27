using System;
using System.ComponentModel;
using System.Text;
using TMPro;
using UnityEngine;
using Google.Protobuf;
using UI.ViewModels;

namespace UI.Views
{
    // 通用的 Proto 自动视图：将 ViewModel 的原生属性以纯文本列表渲染
    public class ProtoAutoView<T> : MonoBehaviour where T : class, IMessage
    {
        [Header("UI References (auto-render)")]
        public TMP_Text ContentText;

        private ProtoAutoViewModel<T> viewModel;
        private bool renderPending = false;

        void Awake()
        {
            viewModel = new ProtoAutoViewModel<T>();
        }

        void Start()
        {
            viewModel.Initialize();
            Bind();
        }

        private void Bind()
        {
            RenderAll();
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            renderPending = true;
        }

        private void RenderAll()
        {
            if (ContentText == null) return;
            var sb = new StringBuilder();
            foreach (var (label, value) in viewModel.Properties)
            {
                sb.AppendLine($"{label}: {value}");
            }
            ContentText.text = sb.ToString();
        }

        void Update()
        {
            if (renderPending)
            {
                RenderAll();
                renderPending = false;
            }
        }

        void OnDestroy()
        {
            if (viewModel != null)
            {
                viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                viewModel.Dispose();
            }
        }
    }
}
