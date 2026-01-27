using System.ComponentModel;
using TMPro;
using UnityEngine;
using UI.ViewModels;

namespace UI.Views
{
    // 明确的 View 基类：统一处理 VM 订阅与主线程刷新
    public abstract class ProtoViewBase<TViewModel> : MonoBehaviour where TViewModel : INotifyPropertyChanged
    {
        protected TViewModel viewModel;
        private bool renderPending;

        protected abstract TViewModel CreateViewModel();
        protected abstract void RenderAll();

        protected virtual void Awake()
        {
            viewModel = CreateViewModel();
        }

        protected virtual void Start()
        {
            // 调用 ViewModel 的 Initialize（如果存在该方法）
            var initMethod = viewModel.GetType().GetMethod("Initialize");
            initMethod?.Invoke(viewModel, null);
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

        protected virtual void Update()
        {
            if (renderPending)
            {
                RenderAll();
                renderPending = false;
            }
        }

        protected virtual void OnDestroy()
        {
            if (viewModel != null)
            {
                viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                var disposeMethod = viewModel.GetType().GetMethod("Dispose");
                disposeMethod?.Invoke(viewModel, null);
            }
        }
    }
}
