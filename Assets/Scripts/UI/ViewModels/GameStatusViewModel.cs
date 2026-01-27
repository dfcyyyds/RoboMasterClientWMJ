using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Framework.Network;
using Google.Protobuf;

namespace UI.ViewModels
{
    public class GameStatusViewModel : INotifyPropertyChanged
    {
        // 按协议定义的全部字段（可供 UI 挂载绑定）
        private uint currentRound;
        private uint totalRounds;
        private uint redScore;
        private uint blueScore;
        private uint currentStage;
        private int stageCountdownSec;
        private int stageElapsedSec;
        private bool isPaused;

        public uint CurrentRound
        {
            get => currentRound;
            set { if (currentRound != value) { currentRound = value; OnPropertyChanged(); } }
        }

        public uint TotalRounds
        {
            get => totalRounds;
            set { if (totalRounds != value) { totalRounds = value; OnPropertyChanged(); } }
        }

        public uint RedScore
        {
            get => redScore;
            set { if (redScore != value) { redScore = value; OnPropertyChanged(); } }
        }

        public uint BlueScore
        {
            get => blueScore;
            set { if (blueScore != value) { blueScore = value; OnPropertyChanged(); } }
        }

        public uint CurrentStage
        {
            get => currentStage;
            set { if (currentStage != value) { currentStage = value; OnPropertyChanged(); } }
        }

        public int StageCountdownSec
        {
            get => stageCountdownSec;
            set { if (stageCountdownSec != value) { stageCountdownSec = value; OnPropertyChanged(); } }
        }

        public int StageElapsedSec
        {
            get => stageElapsedSec;
            set { if (stageElapsedSec != value) { stageElapsedSec = value; OnPropertyChanged(); } }
        }

        public bool IsPaused
        {
            get => isPaused;
            set { if (isPaused != value) { isPaused = value; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Initialize()
        {
            // 初始化一次数据
            var gs = ProtobufManager.Instance.GameStatus;
            UpdateFrom(gs);
            // 订阅数据更新
            ProtobufManager.Instance.OnDataUpdated += OnDataUpdated;
        }

        private void OnDataUpdated(string typeName, object data)
        {
            if (typeName == nameof(GameStatus) && data is IMessage msg)
            {
                UpdateFrom(msg);
            }
        }

        private void UpdateFrom(IMessage msg)
        {
            try
            {
                // 更新字段到 ViewModel
                var gs = msg as GameStatus;
                if (gs != null)
                {
                    CurrentRound = gs.CurrentRound;
                    TotalRounds = gs.TotalRounds;
                    RedScore = gs.RedScore;
                    BlueScore = gs.BlueScore;
                    CurrentStage = gs.CurrentStage;
                    StageCountdownSec = gs.StageCountdownSec;
                    StageElapsedSec = gs.StageElapsedSec;
                    IsPaused = gs.IsPaused;
                }
            }
            catch (Exception)
            {
            }
        }

        public void Dispose()
        {
            ProtobufManager.Instance.OnDataUpdated -= OnDataUpdated;
        }
    }
}
