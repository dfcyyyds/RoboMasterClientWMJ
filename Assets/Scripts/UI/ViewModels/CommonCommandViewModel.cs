using Google.Protobuf;

namespace UI.ViewModels
{
    /// <summary>
    /// 通用指令 ViewModel（V1.2.0 新增）
    /// </summary>
    public class CommonCommandViewModel : ProtoViewModelBase<CommonCommand>
    {
        private uint cmdType;
        private uint param;

        public uint CmdType { get => cmdType; set { if (cmdType != value) { cmdType = value; OnPropertyChanged(); } } }
        public uint Param { get => param; set { if (param != value) { param = value; OnPropertyChanged(); } } }

        protected override void UpdateFrom(CommonCommand msg)
        {
            CmdType = msg.CmdType;
            Param = msg.Param;
        }
    }
}
