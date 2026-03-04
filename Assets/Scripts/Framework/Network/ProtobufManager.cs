using System;
using Google.Protobuf;

namespace Framework.Network
{
    /// <summary>
    /// ProtobufManager 单例，集中管理所有 Protobuf 数据对象。
    /// 支持 Handler 更新、Loxodon 框架数据绑定。
    /// </summary>
    public class ProtobufManager
    {
        // 标准 C# 单例实现
        private static readonly Lazy<ProtobufManager> _instance = new Lazy<ProtobufManager>(() => new ProtobufManager());
        public static ProtobufManager Instance => _instance.Value;
        // 全部33个 Protobuf 数据对象，支持属性变更通知
        public KeyboardMouseControl KeyboardMouseControl { get; private set; } = new KeyboardMouseControl();
        public CustomControl CustomControl { get; private set; } = new CustomControl();
        public GameStatus GameStatus { get; private set; } = new GameStatus();
        public GlobalUnitStatus GlobalUnitStatus { get; private set; } = new GlobalUnitStatus();
        public GlobalLogisticsStatus GlobalLogisticsStatus { get; private set; } = new GlobalLogisticsStatus();
        public GlobalSpecialMechanism GlobalSpecialMechanism { get; private set; } = new GlobalSpecialMechanism();
        public Event Event { get; private set; } = new Event();
        public RobotInjuryStat RobotInjuryStat { get; private set; } = new RobotInjuryStat();
        public RobotRespawnStatus RobotRespawnStatus { get; private set; } = new RobotRespawnStatus();
        public RobotStaticStatus RobotStaticStatus { get; private set; } = new RobotStaticStatus();
        public RobotDynamicStatus RobotDynamicStatus { get; private set; } = new RobotDynamicStatus();
        public RobotModuleStatus RobotModuleStatus { get; private set; } = new RobotModuleStatus();
        public RobotPosition RobotPosition { get; private set; } = new RobotPosition();
        public Buff Buff { get; private set; } = new Buff();
        public PenaltyInfo PenaltyInfo { get; private set; } = new PenaltyInfo();
        public RobotPathPlanInfo RobotPathPlanInfo { get; private set; } = new RobotPathPlanInfo();
        public MapClickInfoNotify MapClickInfoNotify { get; private set; } = new MapClickInfoNotify();
        public RadarInfoToClient RadarInfoToClient { get; private set; } = new RadarInfoToClient();
        public CustomByteBlock CustomByteBlock { get; private set; } = new CustomByteBlock();
        public AssemblyCommand AssemblyCommand { get; private set; } = new AssemblyCommand();
        public TechCoreMotionStateSync TechCoreMotionStateSync { get; private set; } = new TechCoreMotionStateSync();
        public RobotPerformanceSelectionCommand RobotPerformanceSelectionCommand { get; private set; } = new RobotPerformanceSelectionCommand();
        public RobotPerformanceSelectionSync RobotPerformanceSelectionSync { get; private set; } = new RobotPerformanceSelectionSync();
        public HeroDeployModeEventCommand HeroDeployModeEventCommand { get; private set; } = new HeroDeployModeEventCommand();
        public DeployModeStatusSync DeployModeStatusSync { get; private set; } = new DeployModeStatusSync();
        public RuneActivateCommand RuneActivateCommand { get; private set; } = new RuneActivateCommand();
        public RuneStatusSync RuneStatusSync { get; private set; } = new RuneStatusSync();
        public SentryStatusSync SentryStatusSync { get; private set; } = new SentryStatusSync();
        public DartCommand DartCommand { get; private set; } = new DartCommand();
        public DartSelectTargetStatusSync DartSelectTargetStatusSync { get; private set; } = new DartSelectTargetStatusSync();
        public SentryCtrlCommand SentryCtrlCommand { get; private set; } = new SentryCtrlCommand();
        public SentryCtrlResult SentryCtrlResult { get; private set; } = new SentryCtrlResult();
        public CommonCommand CommonCommand { get; private set; } = new CommonCommand();
        public AirSupportCommand AirSupportCommand { get; private set; } = new AirSupportCommand();
        public AirSupportStatusSync AirSupportStatusSync { get; private set; } = new AirSupportStatusSync();

        // 事件：数据变更通知（可用于 Loxodon 框架绑定）
        public event Action<string, object> OnDataUpdated;

        /// <summary>
        /// 通用数据更新接口，供 Handler 调用
        /// </summary>
        /// <typeparam name="T">Protobuf 消息类型</typeparam>
        /// <param name="data">新数据</param>
        /// 用泛型模板与类型反射实现通用更新逻辑，减少重复代码
        public void UpdateData<T>(T data) where T : class
        {
            if (data == null) return;
            var type = typeof(T);
            // 反射赋值到对应属性
            var prop = this.GetType().GetProperty(type.Name);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(this, data);
                OnDataUpdated?.Invoke(type.Name, data);
            }
            else if (prop != null && prop.CanRead)
            {
                // 对于只读属性，尝试 CopyFrom
                var target = prop.GetValue(this) as Google.Protobuf.IMessage;
                var src = data as Google.Protobuf.IMessage;
                if (target != null && src != null)
                {
                    target.MergeFrom(src.ToByteArray());
                    OnDataUpdated?.Invoke(type.Name, target);
                }
            }
        }

        /// <summary>
        /// 获取指定类型的 Protobuf 数据对象
        /// </summary>
        public T GetData<T>() where T : class
        {
            var prop = this.GetType().GetProperty(typeof(T).Name);
            if (prop != null)
                return prop.GetValue(this) as T;
            return null;
        }

        // 可扩展：支持批量更新、快照、回滚等
    }
}