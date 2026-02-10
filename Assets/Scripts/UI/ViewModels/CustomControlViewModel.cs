using Google.Protobuf;

namespace UI.ViewModels
{
    /// <summary>
    /// 自定义控制数据 ViewModel（原 RemoteControl.data 拆分为独立消息）
    /// </summary>
    public class CustomControlViewModel : ProtoViewModelBase<CustomControl>
    {
        private ByteString data = ByteString.Empty;

        public ByteString Data { get => data; set { if (data != value) { data = value; OnPropertyChanged(); } } }

        protected override void UpdateFrom(CustomControl msg)
        {
            Data = msg.Data ?? ByteString.Empty;
        }
    }
}
