using Google.Protobuf;
using System.Collections.Generic;

namespace UI.ViewModels
{
    /// <summary>
    /// 雷达信息 ViewModel
    /// V1.3.0: 从单机器人改为 12 机器人批量模式
    /// 顺序：对方(英雄,工程,3号步兵,4号步兵,空中,哨兵) → 己方(英雄,工程,3号步兵,4号步兵,空中,哨兵)
    /// 坐标单位从米(float)改为厘米(uint32)
    /// </summary>
    public class RadarInfoToClientViewModel : ProtoViewModelBase<RadarInfoToClient>
    {
        /// <summary>12 个机器人的雷达信息（按固定顺序）</summary>
        public List<RadarRobotInfo> RobotInfoList { get; private set; } = new List<RadarRobotInfo>();

        protected override void UpdateFrom(RadarInfoToClient msg)
        {
            RobotInfoList.Clear();
            foreach (var info in msg.RobotInfo)
            {
                RobotInfoList.Add(new RadarRobotInfo
                {
                    TargetPosX = info.TargetPosX,
                    TargetPosY = info.TargetPosY,
                    IsHighLight = info.IsHighLight,
                });
            }
            OnPropertyChanged(nameof(RobotInfoList));
        }
    }

    /// <summary>单个机器人雷达信息（坐标单位：厘米）</summary>
    public class RadarRobotInfo
    {
        public uint TargetPosX { get; set; }
        public uint TargetPosY { get; set; }
        public uint IsHighLight { get; set; }
    }
}
