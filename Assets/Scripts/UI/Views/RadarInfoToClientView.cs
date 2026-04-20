using TMPro;
using UI.ViewModels;
using UnityEngine;

namespace UI.Views
{
    /// <summary>
    /// V1.3.0: 雷达信息改为 12 机器人批量模式，此视图显示摘要信息
    /// </summary>
    public class RadarInfoToClientView : ProtoViewBase<RadarInfoToClientViewModel>
    {
        public TMP_Text RobotCountText;
        public TMP_Text RobotInfoSummaryText;

        protected override RadarInfoToClientViewModel CreateViewModel() => new RadarInfoToClientViewModel();

        protected override void RenderAll()
        {
            var vm = viewModel;
            if (RobotCountText) RobotCountText.text = vm.RobotInfoList.Count.ToString();
            if (RobotInfoSummaryText)
            {
                var sb = new System.Text.StringBuilder();
                string[] names = { "对方英雄", "对方工程", "对方3号", "对方4号", "对方空中", "对方哨兵",
                                   "己方英雄", "己方工程", "己方3号", "己方4号", "己方空中", "己方哨兵" };
                for (int i = 0; i < vm.RobotInfoList.Count && i < names.Length; i++)
                {
                    var info = vm.RobotInfoList[i];
                    if (info.TargetPosX > 0 || info.TargetPosY > 0)
                    {
                        sb.AppendLine($"{names[i]}: ({info.TargetPosX},{info.TargetPosY})cm {(info.IsHighLight > 0 ? "★" : "")}");
                    }
                }
                RobotInfoSummaryText.text = sb.ToString();
            }
        }
    }
}
