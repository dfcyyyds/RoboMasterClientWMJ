using System;

namespace UI.RobotSelection
{
    /// <summary>
    /// Team color enumeration
    /// </summary>
    public enum TeamColor
    {
        Red = 0,
        Blue = 1
    }

    /// <summary>
    /// Robot type enumeration (RoboMaster rules)
    /// </summary>
    public enum RobotType
    {
        Hero = 1,           // Hero
        Engineer = 2,       // Engineer
        Infantry3 = 3,      // Infantry #3
        Infantry4 = 4,      // Infantry #4
        Infantry5 = 5,      // Infantry #5
        Aerial = 6,         // Aerial robot
        Sentry = 7,         // Sentry
        Dart = 8,           // Dart launcher
        Radar = 9           // Radar station
    }

    /// <summary>
    /// Robot selection result
    /// </summary>
    public class RobotSelectionResult
    {
        public TeamColor Team { get; set; }
        public RobotType Robot { get; set; }
        public int RobotId => (int)Team * 100 + (int)Robot; // Red: 1-9, Blue: 101-109

        public override string ToString()
        {
            string teamName = Team == TeamColor.Red ? "红方" : "蓝方";
            string robotName = Robot switch
            {
                RobotType.Hero => "英雄",
                RobotType.Engineer => "工程",
                RobotType.Infantry3 => "3号步兵",
                RobotType.Infantry4 => "4号步兵",
                RobotType.Infantry5 => "5号步兵",
                RobotType.Aerial => "空中机器人",
                RobotType.Sentry => "哨兵",
                RobotType.Dart => "飞镖",
                RobotType.Radar => "雷达站",
                _ => "未知"
            };
            return $"{teamName} - {robotName} (ID: {RobotId})";
        }
    }

    /// <summary>
    /// Robot selection completed event args
    /// </summary>
    public class RobotSelectionEventArgs : EventArgs
    {
        public RobotSelectionResult Result { get; }
        public RobotSelectionEventArgs(RobotSelectionResult result) => Result = result;
    }
}
