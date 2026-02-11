namespace Framework.Network
{
    public enum GameEventId
    {
        MatchStart = 1,
        MatchEnd = 2,
        StageChange = 3,
        MatchPaused = 4,
        MatchResumed = 5,

        RobotKilled = 10,
        RobotRespawn = 11,
        RobotInstantRespawn = 12,
        RobotOffline = 13,
        RobotReconnect = 14,

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
