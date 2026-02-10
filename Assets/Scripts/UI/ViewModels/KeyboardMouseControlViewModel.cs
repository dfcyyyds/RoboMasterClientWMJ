using Google.Protobuf;

namespace UI.ViewModels
{
    /// <summary>
    /// 键鼠控制 ViewModel（原 RemoteControlViewModel，V1.2.0 拆分后去除 Data 字段）
    /// </summary>
    public class KeyboardMouseControlViewModel : ProtoViewModelBase<KeyboardMouseControl>
    {
        private int mouseX;
        private int mouseY;
        private int mouseZ;
        private bool leftButtonDown;
        private bool rightButtonDown;
        private uint keyboardValue;
        private bool midButtonDown;

        public int MouseX { get => mouseX; set { if (mouseX != value) { mouseX = value; OnPropertyChanged(); } } }
        public int MouseY { get => mouseY; set { if (mouseY != value) { mouseY = value; OnPropertyChanged(); } } }
        public int MouseZ { get => mouseZ; set { if (mouseZ != value) { mouseZ = value; OnPropertyChanged(); } } }
        public bool LeftButtonDown { get => leftButtonDown; set { if (leftButtonDown != value) { leftButtonDown = value; OnPropertyChanged(); } } }
        public bool RightButtonDown { get => rightButtonDown; set { if (rightButtonDown != value) { rightButtonDown = value; OnPropertyChanged(); } } }
        public uint KeyboardValue { get => keyboardValue; set { if (keyboardValue != value) { keyboardValue = value; OnPropertyChanged(); } } }
        public bool MidButtonDown { get => midButtonDown; set { if (midButtonDown != value) { midButtonDown = value; OnPropertyChanged(); } } }

        protected override void UpdateFrom(KeyboardMouseControl msg)
        {
            MouseX = msg.MouseX;
            MouseY = msg.MouseY;
            MouseZ = msg.MouseZ;
            LeftButtonDown = msg.LeftButtonDown;
            RightButtonDown = msg.RightButtonDown;
            KeyboardValue = msg.KeyboardValue;
            MidButtonDown = msg.MidButtonDown;
        }
    }
}
