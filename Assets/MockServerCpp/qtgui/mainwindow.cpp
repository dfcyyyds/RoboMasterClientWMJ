#include "mainwindow.h"

#include <QHeaderView>
#include <QProgressBar>
#include <QVBoxLayout>

MainWindow::MainWindow(QWidget* parent) : QMainWindow(parent) {
  QWidget* central = new QWidget(this);
  QVBoxLayout* layout = new QVBoxLayout(central);
  messageTable = new QTableWidget(this);
  messageTable->setColumnCount(3);
  messageTable->setHorizontalHeaderLabels({"消息类型", "发送值", "接收值"});
  messageTable->horizontalHeader()->setSectionResizeMode(QHeaderView::Stretch);
  logEdit = new QTextEdit(this);
  logEdit->setReadOnly(true);
  statsTable = new QTableWidget(0, 3, this);
  statsTable->setHorizontalHeaderLabels({"消息类型", "已发送", "已接收"});
  statsTable->horizontalHeader()->setSectionResizeMode(QHeaderView::Stretch);
  layout->addWidget(messageTable);
  layout->addWidget(statsTable);
  layout->addWidget(logEdit);
  setCentralWidget(central);
  refreshTimer = new QTimer(this);
  connect(refreshTimer, &QTimer::timeout, this, &MainWindow::refreshUI);
  refreshTimer->start(1000);  // 降低刷新频率，减轻UI压力
}

MainWindow::~MainWindow() {}

void MainWindow::updateMessageTable(const QString& type,
                                    const QVariantMap& values, bool isSend) {
  if (!msgRecords.contains(type)) {
    msgRecords[type] = MsgRecord();
  }
  if (isSend) {
    msgRecords[type].send = values;
  } else {
    msgRecords[type].recv = values;
  }
}

void MainWindow::updateLog(const QString& log) { logEdit->append(log); }

void MainWindow::updateStats(const QString& type, int sent, int received) {
  stats[type] = qMakePair(sent, received);
}

void MainWindow::refreshUI() {
  // 优化：区分显示消息方向
  messageTable->setRowCount(msgRecords.size());
  int row = 0;
  for (auto it = msgRecords.begin(); it != msgRecords.end(); ++it, ++row) {
    QString msgType = it.key();
    QString sendStr, recvStr;
    // 发送值：每个字段一行，键:值
    const QVariantMap& sendMap = it.value().send;
    for (auto skey = sendMap.constBegin(); skey != sendMap.constEnd(); ++skey) {
      sendStr += skey.key() + ": " + QVariant(skey.value()).toString() + "\n";
    }
    // 接收值：每个字段一行，键:值
    const QVariantMap& recvMap = it.value().recv;
    for (auto rkey = recvMap.constBegin(); rkey != recvMap.constEnd(); ++rkey) {
      recvStr += rkey.key() + ": " + QVariant(rkey.value()).toString() + "\n";
    }
    // 判断类型归属
    QString direction;
    static QStringList serverToClientTypes = {"GameStatus",
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
                                              "MapClickInfoNotify",
                                              "RaderInfoToClient",
                                              "CustomByteBlock",
                                              "TechCoreMotionStateSync",
                                              "RobotPerformanceSelectionSync",
                                              "DeployModeStatusSync",
                                              "RuneStatusSync",
                                              "SentinelStatusSync",
                                              "DartSelectTargetStatusSync",
                                              "GuardCtrlResult",
                                              "AirSupportStatusSync"};
    static QStringList clientToServerTypes = {
        "AssemblyCommand",
        "RobotPerformanceSelectionCommand",
        "HeroDeployModeEventCommand",
        "RuneActivateCommand",
        "DartCommand",
        "GuardCtrlCommand",
        "AirSupportCommand"};
    if (serverToClientTypes.contains(msgType)) {
      direction = "服务器->客户端";
    } else if (clientToServerTypes.contains(msgType)) {
      direction = "客户端->服务器";
    } else {
      direction = "未知";
    }
    messageTable->setItem(
        row, 0, new QTableWidgetItem(msgType + " (" + direction + ")"));
    messageTable->setItem(row, 1, new QTableWidgetItem(sendStr.trimmed()));
    messageTable->setItem(row, 2, new QTableWidgetItem(recvStr.trimmed()));
  }
  // 刷新统计表，使用进度条展示频率
  statsTable->setRowCount(stats.size());
  int srow = 0;
  int maxCount = 1;
  for (auto it = stats.begin(); it != stats.end(); ++it) {
    maxCount =
        std::max(maxCount, std::max(it.value().first, it.value().second));
  }
  for (auto it = stats.begin(); it != stats.end(); ++it, ++srow) {
    statsTable->setItem(srow, 0, new QTableWidgetItem(it.key()));
    // 发送进度条复用
    QProgressBar* sendBar =
        qobject_cast<QProgressBar*>(statsTable->cellWidget(srow, 1));
    if (!sendBar) {
      sendBar = new QProgressBar();
      statsTable->setCellWidget(srow, 1, sendBar);
    }
    sendBar->setRange(0, maxCount);
    sendBar->setValue(it.value().first);
    double percent = maxCount > 0 ? (double)it.value().first / maxCount : 0.0;
    int blueStart = 180;
    int blueEnd = 230;
    int blueVal = blueStart + int((blueEnd - blueStart) * percent);
    if (it.value().first == 0) {
      sendBar->setStyleSheet(
          "QProgressBar::chunk { background: #e57373; border-radius: 6px; } "
          "QProgressBar { background: #f5f5f5; border: 1px solid #bdbdbd; "
          "height: 14px; text-align: center; font: 10pt 'Segoe UI', 'Arial'; "
          "color: #333; }");
    } else {
      sendBar->setStyleSheet(
          QString("QProgressBar::chunk { background: qlineargradient(x1:0, "
                  "y1:0, x2:1, y2:0, stop:0 #b3c6e7, stop:1 rgb(100,150,%1)); "
                  "border-radius: 6px; } QProgressBar { background: #f5f5f5; "
                  "border: 1px solid #bdbdbd; height: 14px; text-align: "
                  "center; font: 10pt 'Segoe UI', 'Arial'; color: #333; }")
              .arg(blueVal));
    }
    // 接收进度条复用
    QProgressBar* recvBar =
        qobject_cast<QProgressBar*>(statsTable->cellWidget(srow, 2));
    if (!recvBar) {
      recvBar = new QProgressBar();
      statsTable->setCellWidget(srow, 2, recvBar);
    }
    recvBar->setRange(0, maxCount);
    recvBar->setValue(it.value().second);
    double percent2 = maxCount > 0 ? (double)it.value().second / maxCount : 0.0;
    int blueVal2 = blueStart + int((blueEnd - blueStart) * percent2);
    if (it.value().second == 0) {
      recvBar->setStyleSheet(
          "QProgressBar::chunk { background: #e57373; border-radius: 6px; } "
          "QProgressBar { background: #f5f5f5; border: 1px solid #bdbdbd; "
          "height: 14px; text-align: center; font: 10pt 'Segoe UI', 'Arial'; "
          "color: #333; }");
    } else {
      recvBar->setStyleSheet(
          QString("QProgressBar::chunk { background: qlineargradient(x1:0, "
                  "y1:0, x2:1, y2:0, stop:0 #b3c6e7, stop:1 rgb(100,150,%1)); "
                  "border-radius: 6px; } QProgressBar { background: #f5f5f5; "
                  "border: 1px solid #bdbdbd; height: 14px; text-align: "
                  "center; font: 10pt 'Segoe UI', 'Arial'; color: #333; }")
              .arg(blueVal2));
    }
  }
  // 可扩展：自动滚动日志、刷新消息表等
}
