#pragma once
#include <QObject>
#include <QString>
#include <QTimer>
#include <QVariantMap>

class Worker : public QObject {
  Q_OBJECT
 public:
  explicit Worker(QObject* parent = nullptr);
 public slots:
  void startWork();  // 启动业务循环
 signals:
  void messageUpdated(const QString& type, const QVariantMap& values,
                      bool isSend);
  void logUpdated(const QString& log);
  void statsUpdated(const QString& type, int sent, int received);
};
