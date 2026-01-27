using System;
using System.ComponentModel;
using UnityEngine;
using TMPro;
using UI.ViewModels;

namespace UI.Views
{
    public class GameStatusView : MonoBehaviour
    {
        [Header("UI References (protocol fields only)")]
        // 分拆字段（逐项渲染，仅协议属性）
        public TMP_Text RoundText;       // 当前回合/总回合
        public TMP_Text RedScoreText;       // 红方分数
        public TMP_Text BlueScoreText;      // 蓝方分数
        public TMP_Text StageText;       // 当前阶段
        public TMP_Text CountdownText;   // 阶段倒计时
        public TMP_Text ElapsedText;     // 阶段已用时
        public TMP_Text PausedText;      // 暂停状态
        // 不再暴露 Title/UpdateTime/JSON 等自定义属性

        private GameStatusViewModel viewModel;
        private bool renderPending = false; // 标记需在主线程刷新

        void Awake()
        {
            viewModel = new GameStatusViewModel();
        }

        void Start()
        {
            // 初始化 ViewModel 并绑定 UI
            viewModel.Initialize();
            Bind(viewModel);
        }

        private void Bind(GameStatusViewModel vm)
        {
            // 首次渲染
            RenderAll(vm);
            // 订阅属性变化以刷新 UI
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // 注意：VM 的变化可能来自 MQTT 背景线程，不能直接改 Unity 对象
            // 这里只做标记，实际刷新放到 Update() 主线程执行
            renderPending = true;
        }

        private void RenderAll(GameStatusViewModel vm)
        {
            // 逐项渲染各字段
            if (RoundText) RoundText.text = $"{vm.CurrentRound}/{vm.TotalRounds}";
            if (RedScoreText) RedScoreText.text = $"{vm.RedScore}";
            if (BlueScoreText) BlueScoreText.text = $"{vm.BlueScore}";
            if (StageText) StageText.text = vm.CurrentStage.ToString();
            if (CountdownText) CountdownText.text = vm.StageCountdownSec / 60 + " : " + vm.StageCountdownSec % 60;
            if (ElapsedText) ElapsedText.text = vm.StageElapsedSec + "s";
            if (PausedText) PausedText.text = vm.IsPaused ? "Yes" : "No";
        }

        void Update()
        {
            if (renderPending && viewModel != null)
            {
                RenderAll(viewModel);
                renderPending = false;
            }
        }

        void OnDestroy()
        {
            if (viewModel != null)
            {
                viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                viewModel.Dispose();
            }
        }
    }
}
