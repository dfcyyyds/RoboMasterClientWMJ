namespace Framework.Network
{
    /// <summary>
    /// 比赛事件枚举
    /// </summary>
    public enum GameEventId
    {
        /// <summary>
        /// 比赛进程相关事件
        /// </summary>
        MatchStart = 1, // 比赛开始
        MatchEnd = 2,   // 比赛结束
        StageChange = 3,  // 阶段改变
        MatchPaused = 4,  // 比赛暂停
        MatchResumed = 5,  // 比赛恢复

        /// <summary>
        /// 车相关事件
        /// </summary>
        RobotKilled = 10,  // 车被击杀
        RobotRespawn = 11,  // 车复活
        RobotInstantRespawn = 12,  // 车复活（瞬间复活）
        RobotOffline = 13,  // 车离线
        RobotReconnect = 14,  // 车重新连接

        /// <summary>
        /// 前哨站相关事件
        /// </summary>
        OutpostDestroyed = 20,
        OutpostRebuilt = 21,
        BaseShieldOpen = 22,
        BaseDestroyed = 23,
        OutpostArmorStop = 24,

        RuneSmallActivating = 30,
        RuneSmallActivated = 31,
        RuneLargeActivating = 32,
        RuneLargeActivated = 33,
        RuneActivateFailed = 34,
        RuneBuffExpired = 35,

        TechCoreAssembling = 40,
        TechCoreAssembled = 41,
        TechCoreFailed = 42,
        TechCoreLevelUp = 43,

        DartGateOpen = 50,
        DartLaunched = 51,
        DartHitOutpost = 52,
        DartHitBase = 53,
        DartGateClose = 54,
        DartScreenBlocked = 55,

        AirSupportStart = 60,
        AirSupportEnd = 61,
        AerialLocked = 62,
        AerialLockReleased = 63,

        PenaltyYellow = 70,
        PenaltyRed = 71,
        PenaltyWarning = 72,

        BuffGained = 80,
        BuffExpired = 81,
        DefenseZoneCaptured = 82,
        DefenseZoneLost = 83,

        AmmoExchanged = 90,
        RemoteHeal = 91,
        GoldIncome = 92,
        SentrySupplyAmmo = 93,

        HeroDeployEnter = 100,
        HeroDeployExit = 101,

        SentryPostureChange = 110,

        RadarMarkThreshold = 120,
        RadarDoubleVuln = 121,

        LevelUp = 130,
        MaxLevelReached = 131,

        EnergySavingMode = 140,
        EnergyBoostMode = 141,
        ChassisPowerCut = 142,

        EngineerDefenseStart = 150,
        EngineerDefenseEnd = 151,

        FirstBlood = 200,
        MultiKill = 201,
        Comeback = 202
    }
}
