// 服务器->自定义客户端 消息类型列表
// 参考 RoboMasterClientMessage.proto V1.2.0
// 仅服务器->自定义客户端方向的topic列表

const std::vector<std::string> server_to_client_types = {
    "GameStatus",
    "GlobalUnitStatus",
    "GlobalLogisticsStatus",
    "GlobalSpecialMechanism",
    "Event",
    "RobotInjuryStat",
    "RobotRespawnStatus",
    "RobotStaticStatus",
    "RobotDynamicStatus",
    "RobotModuleStatus",
    "RobotPosition",
    "Buff",
    "PenaltyInfo",
    "RobotPathPlanInfo",
    "RadarInfoToClient",
    "TechCoreMotionStateSync",
    "RobotPerformanceSelectionSync",
    "DeployModeStatusSync",
    "RuneStatusSync",
    "SentryStatusSync",
    "DartSelectTargetStatusSync",
    "SentryCtrlResult",
    "AirSupportStatusSync",
    "CustomByteBlock"};
