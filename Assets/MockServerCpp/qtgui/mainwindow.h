#pragma once
#include <QMainWindow>
#include <QMap>
#include <QString>
#include <QTabWidget>
#include <QTableWidget>
#include <QTextEdit>
#include <QTimer>
#include <QVariant>

class MainWindow : public QMainWindow {
  Q_OBJECT
 public:
  MainWindow(QWidget* parent = nullptr);
  ~MainWindow();
 public slots:
  void updateMessageTable(const QString& type, const QVariantMap& values,
                          bool isSend);
  void updateLog(const QString& log);
  void updateStats(const QString& type, int sent, int received);
 private slots:
  void refreshUI();

 private:
  QTableWidget* messageTable;  // 总表
  QTextEdit* logEdit;
  QTableWidget* statsTable;
  QTimer* refreshTimer;
  struct MsgRecord {
    QVariantMap send;
    QVariantMap recv;
  };
  QMap<QString, MsgRecord> msgRecords;   // type -> 发送/接收内容
  QMap<QString, QPair<int, int>> stats;  // type -> (sent, received)
};
