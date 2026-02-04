using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace UI.RobotSelection
{
    /// <summary>
    /// Robot Selection ViewModel - manages selection state and business logic
    /// </summary>
    public class RobotSelectionViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler<RobotSelectionEventArgs> SelectionCompleted;

        private TeamColor _selectedTeam = TeamColor.Red;
        private RobotType? _selectedRobot = null;
        private bool _canConfirm = false;
        private string _statusText = "请先选择阵营";

        /// <summary>
        /// Currently selected team
        /// </summary>
        public TeamColor SelectedTeam
        {
            get => _selectedTeam;
            set
            {
                if (_selectedTeam != value)
                {
                    _selectedTeam = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsRedSelected));
                    OnPropertyChanged(nameof(IsBlueSelected));
                    UpdateStatus();
                }
            }
        }

        /// <summary>
        /// Currently selected robot type
        /// </summary>
        public RobotType? SelectedRobot
        {
            get => _selectedRobot;
            set
            {
                if (_selectedRobot != value)
                {
                    _selectedRobot = value;
                    OnPropertyChanged();
                    UpdateStatus();
                }
            }
        }

        /// <summary>
        /// Whether selection can be confirmed
        /// </summary>
        public bool CanConfirm
        {
            get => _canConfirm;
            private set
            {
                if (_canConfirm != value)
                {
                    _canConfirm = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Status hint text
        /// </summary>
        public string StatusText
        {
            get => _statusText;
            private set
            {
                if (_statusText != value)
                {
                    _statusText = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsRedSelected => SelectedTeam == TeamColor.Red;
        public bool IsBlueSelected => SelectedTeam == TeamColor.Blue;

        /// <summary>
        /// Select red team
        /// </summary>
        public void SelectRed() => SelectedTeam = TeamColor.Red;

        /// <summary>
        /// Select blue team
        /// </summary>
        public void SelectBlue() => SelectedTeam = TeamColor.Blue;

        /// <summary>
        /// Select robot type
        /// </summary>
        public void SelectRobot(RobotType robot)
        {
            SelectedRobot = robot;
        }

        /// <summary>
        /// Confirm selection
        /// </summary>
        public void Confirm()
        {
            if (!CanConfirm || !SelectedRobot.HasValue)
            {
                return;
            }

            var result = new RobotSelectionResult
            {
                Team = SelectedTeam,
                Robot = SelectedRobot.Value
            };

            wmj.DebugTools.Info($"[RobotSelection] 用户确认选择: {result}", wmj.DebugTools.LogCategory.UI);
            SelectionCompleted?.Invoke(this, new RobotSelectionEventArgs(result));
        }

        private void UpdateStatus()
        {
            if (!SelectedRobot.HasValue)
            {
                StatusText = $"已选择{(SelectedTeam == TeamColor.Red ? "红方" : "蓝方")}，请选择兵种";
                CanConfirm = false;
            }
            else
            {
                string teamName = SelectedTeam == TeamColor.Red ? "红方" : "蓝方";
                string robotName = GetRobotDisplayName(SelectedRobot.Value);
                StatusText = $"已选择: {teamName} - {robotName}";
                CanConfirm = true;
            }
        }

        private string GetRobotDisplayName(RobotType robot)
        {
            return robot switch
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
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
