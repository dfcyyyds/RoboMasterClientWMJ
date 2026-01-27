using Google.Protobuf;

namespace UI.ViewModels
{
    public class CustomByteBlockViewModel : ProtoViewModelBase<CustomByteBlock>
    {
        private ByteString data = ByteString.Empty;

        public ByteString Data { get => data; set { if (data != value) { data = value; OnPropertyChanged(); } } }

        protected override void UpdateFrom(CustomByteBlock msg)
        {
            Data = msg.Data ?? ByteString.Empty;
        }
    }
}
