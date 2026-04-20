using Google.Protobuf;

namespace UI.ViewModels
{
    public class TechCoreMotionStateSyncViewModel : ProtoViewModelBase<TechCoreMotionStateSync>
    {
        private uint maximumDifficultyLevel;
        private uint basicState;
        private uint putinState;
        private uint moveState;
        private uint rotateState;
        private uint enemyCoreStatus;
        private uint remainTimeAll;
        private uint remainTimeStep;

        public uint MaximumDifficultyLevel { get => maximumDifficultyLevel; set { if (maximumDifficultyLevel != value) { maximumDifficultyLevel = value; OnPropertyChanged(); } } }
        /// <summary>V1.3.0: 原 status → basic_state (1=初始位置, 2=运动中, 3=已到达对应位姿)</summary>
        public uint BasicState { get => basicState; set { if (basicState != value) { basicState = value; OnPropertyChanged(); } } }
        /// <summary>V1.3.0 新增: 能量单元放入状态</summary>
        public uint PutinState { get => putinState; set { if (putinState != value) { putinState = value; OnPropertyChanged(); } } }
        /// <summary>V1.3.0 新增: 能量单元平移状态</summary>
        public uint MoveState { get => moveState; set { if (moveState != value) { moveState = value; OnPropertyChanged(); } } }
        /// <summary>V1.3.0 新增: 能量单元旋转状态</summary>
        public uint RotateState { get => rotateState; set { if (rotateState != value) { rotateState = value; OnPropertyChanged(); } } }
        public uint EnemyCoreStatus { get => enemyCoreStatus; set { if (enemyCoreStatus != value) { enemyCoreStatus = value; OnPropertyChanged(); } } }
        public uint RemainTimeAll { get => remainTimeAll; set { if (remainTimeAll != value) { remainTimeAll = value; OnPropertyChanged(); } } }
        public uint RemainTimeStep { get => remainTimeStep; set { if (remainTimeStep != value) { remainTimeStep = value; OnPropertyChanged(); } } }

        protected override void UpdateFrom(TechCoreMotionStateSync msg)
        {
            MaximumDifficultyLevel = msg.MaximumDifficultyLevel;
            BasicState = msg.BasicState;
            PutinState = msg.PutinState;
            MoveState = msg.MoveState;
            RotateState = msg.RotateState;
            EnemyCoreStatus = msg.EnemyCoreStatus;
            RemainTimeAll = msg.RemainTimeAll;
            RemainTimeStep = msg.RemainTimeStep;
        }
    }
}
