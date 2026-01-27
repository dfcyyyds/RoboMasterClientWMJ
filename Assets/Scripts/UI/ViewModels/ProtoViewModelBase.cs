using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Framework.Network;
using Google.Protobuf;

namespace UI.ViewModels
{
    // 明确的 MVVM 基类：管理订阅与属性通知，不做反射
    public abstract class ProtoViewModelBase<TMsg> : INotifyPropertyChanged where TMsg : class, IMessage
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public void Initialize()
        {
            var data = ProtobufManager.Instance.GetData<TMsg>();
            if (data != null)
            {
                UpdateFrom(data);
            }
            ProtobufManager.Instance.OnDataUpdated += OnDataUpdated;
        }

        private void OnDataUpdated(string typeName, object data)
        {
            if (typeName == typeof(TMsg).Name && data is TMsg msg)
            {
                UpdateFrom(msg);
            }
        }

        public void Dispose()
        {
            ProtobufManager.Instance.OnDataUpdated -= OnDataUpdated;
        }

        // 由具体 ViewModel 实现将协议字段映射到属性
        protected abstract void UpdateFrom(TMsg msg);
    }
}
