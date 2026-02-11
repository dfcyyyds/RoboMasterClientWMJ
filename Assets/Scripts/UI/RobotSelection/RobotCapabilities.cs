using System.Collections.Generic;

namespace UI.RobotSelection
{
    /// <summary>
    /// 兵种能力定义 — 根据 RoboMaster 2026 规则定义各兵种的功能特性
    /// 用于判断是否需要弹出体系选择UI、是否隐藏射击相关UI等
    /// </summary>
    public static class RobotCapabilities
    {
        /// <summary>性能体系选项</summary>
        public enum ShooterPerf : uint
        {
            None = 0,       // 不支持
            BurstPriority = 1,    // 爆发优先
            CooldownPriority = 2  // 冷却优先
        }

        /// <summary>底盘体系选项</summary>
        public enum ChassisPerf : uint
        {
            None = 0,       // 不支持
            PowerPriority = 1,    // 功率优先
            HealthPriority = 2    // 血量优先
        }

        /// <summary>哨兵控制模式</summary>
        public enum SentryCtrl : uint
        {
            Auto = 0,      // 全自动
            SemiAuto = 1   // 半自动
        }

        /// <summary>
        /// 单个兵种的能力描述
        /// </summary>
        public class RobotProfile
        {
            /// <summary>兵种类型</summary>
            public RobotType Type { get; set; }

            /// <summary>显示名</summary>
            public string DisplayName { get; set; }

            /// <summary>是否具备射击能力（用于决定是否显示准星/热量/弹药UI）</summary>
            public bool CanShoot { get; set; }

            /// <summary>是否支持性能体系（射手）选择</summary>
            public bool HasShooterPerf { get; set; }

            /// <summary>是否支持底盘体系选择</summary>
            public bool HasChassisPerf { get; set; }

            /// <summary>是否支持哨兵控制模式选择（仅哨兵）</summary>
            public bool HasSentryControl { get; set; }

            /// <summary>弹丸类型描述（用于UI显示）</summary>
            public string AmmoType { get; set; }
        }

        // ─── 所有兵种能力查表 ───

        private static readonly Dictionary<RobotType, RobotProfile> _profiles =
            new Dictionary<RobotType, RobotProfile>
        {
            {
                RobotType.Hero, new RobotProfile
                {
                    Type = RobotType.Hero,
                    DisplayName = "英雄",
                    CanShoot = true,
                    HasShooterPerf = true,
                    HasChassisPerf = true,
                    HasSentryControl = false,
                    AmmoType = "42mm"
                }
            },
            {
                RobotType.Engineer, new RobotProfile
                {
                    Type = RobotType.Engineer,
                    DisplayName = "工程",
                    CanShoot = false,
                    HasShooterPerf = false,
                    HasChassisPerf = true,
                    HasSentryControl = false,
                    AmmoType = null
                }
            },
            {
                RobotType.Infantry3, new RobotProfile
                {
                    Type = RobotType.Infantry3,
                    DisplayName = "3号步兵",
                    CanShoot = true,
                    HasShooterPerf = true,
                    HasChassisPerf = true,
                    HasSentryControl = false,
                    AmmoType = "17mm"
                }
            },
            {
                RobotType.Infantry4, new RobotProfile
                {
                    Type = RobotType.Infantry4,
                    DisplayName = "4号步兵",
                    CanShoot = true,
                    HasShooterPerf = true,
                    HasChassisPerf = true,
                    HasSentryControl = false,
                    AmmoType = "17mm"
                }
            },
            {
                RobotType.Infantry5, new RobotProfile
                {
                    Type = RobotType.Infantry5,
                    DisplayName = "5号步兵",
                    CanShoot = true,
                    HasShooterPerf = true,
                    HasChassisPerf = true,
                    HasSentryControl = false,
                    AmmoType = "17mm"
                }
            },
            {
                RobotType.Aerial, new RobotProfile
                {
                    Type = RobotType.Aerial,
                    DisplayName = "空中机器人",
                    CanShoot = true,
                    HasShooterPerf = true,
                    HasChassisPerf = false,
                    HasSentryControl = false,
                    AmmoType = "17mm"
                }
            },
            {
                RobotType.Sentry, new RobotProfile
                {
                    Type = RobotType.Sentry,
                    DisplayName = "哨兵",
                    CanShoot = true,
                    HasShooterPerf = false,
                    HasChassisPerf = false,
                    HasSentryControl = true,
                    AmmoType = "17mm"
                }
            },
            {
                RobotType.Dart, new RobotProfile
                {
                    Type = RobotType.Dart,
                    DisplayName = "飞镖",
                    CanShoot = false,
                    HasShooterPerf = false,
                    HasChassisPerf = false,
                    HasSentryControl = false,
                    AmmoType = null
                }
            },
            {
                RobotType.Radar, new RobotProfile
                {
                    Type = RobotType.Radar,
                    DisplayName = "雷达站",
                    CanShoot = false,
                    HasShooterPerf = false,
                    HasChassisPerf = false,
                    HasSentryControl = false,
                    AmmoType = null
                }
            }
        };

        /// <summary>获取指定兵种的能力信息</summary>
        public static RobotProfile GetProfile(RobotType type)
        {
            return _profiles.TryGetValue(type, out var p) ? p : null;
        }

        /// <summary>该兵种是否需要显示体系选择面板</summary>
        public static bool NeedsPerformanceSelection(RobotType type)
        {
            var p = GetProfile(type);
            return p != null && (p.HasShooterPerf || p.HasChassisPerf || p.HasSentryControl);
        }

        /// <summary>该兵种是否具备射击能力</summary>
        public static bool CanShoot(RobotType type)
        {
            var p = GetProfile(type);
            return p != null && p.CanShoot;
        }
    }
}
