using Google.Protobuf;

namespace UI.ViewModels
{
    public class RemoteControlViewModel : ProtoViewModelBase<RemoteControl>
    {
        private int mouseX;
        private int mouseY;
        private int mouseZ;
        private bool leftButtonDown;
        private bool rightButtonDown;
        private uint keyboardValue;
        private bool midButtonDown;
        private ByteString data = ByteString.Empty;

        public int MouseX { get => mouseX; set { if (mouseX != value) { mouseX = value; OnPropertyChanged(); } } }
        public int MouseY { get => mouseY; set { if (mouseY != value) { mouseY = value; OnPropertyChanged(); } } }
        public int MouseZ { get => mouseZ; set { if (mouseZ != value) { mouseZ = value; OnPropertyChanged(); } } }
        public bool LeftButtonDown { get => leftButtonDown; set { if (leftButtonDown != value) { leftButtonDown = value; OnPropertyChanged(); } } }
        public bool RightButtonDown { get => rightButtonDown; set { if (rightButtonDown != value) { rightButtonDown = value; OnPropertyChanged(); } } }
        public uint KeyboardValue { get => keyboardValue; set { if (keyboardValue != value) { keyboardValue = value; OnPropertyChanged(); } } }
        public bool MidButtonDown { get => midButtonDown; set { if (midButtonDown != value) { midButtonDown = value; OnPropertyChanged(); } } }
        public ByteString Data { get => data; set { if (data != value) { data = value; OnPropertyChanged(); } } }

        protected override void UpdateFrom(RemoteControl msg)
        {
            MouseX = msg.MouseX;
            MouseY = msg.MouseY;
            MouseZ = msg.MouseZ;
            LeftButtonDown = msg.LeftButtonDown;
            RightButtonDown = msg.RightButtonDown;
            KeyboardValue = msg.KeyboardValue;
            MidButtonDown = msg.MidButtonDown;
            Data = msg.Data ?? ByteString.Empty;
        }
    }
}
