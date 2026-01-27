#include <QApplication>
#include <QThread>

#include "mainwindow.h"
#include "worker.h"

int main(int argc, char* argv[]) {
  QApplication a(argc, argv);
  MainWindow w;
  QThread* workerThread = new QThread;
  Worker* worker = new Worker();
  worker->moveToThread(workerThread);
  QObject::connect(workerThread, &QThread::started, worker, &Worker::startWork);
  QObject::connect(worker, &Worker::messageUpdated, &w,
                   &MainWindow::updateMessageTable);
  QObject::connect(worker, &Worker::logUpdated, &w, &MainWindow::updateLog);
  QObject::connect(worker, &Worker::statsUpdated, &w, &MainWindow::updateStats);
  QObject::connect(&a, &QApplication::aboutToQuit, workerThread,
                   &QThread::quit);
  QObject::connect(workerThread, &QThread::finished, worker,
                   &QObject::deleteLater);
  QObject::connect(workerThread, &QThread::finished, workerThread,
                   &QObject::deleteLater);
  workerThread->start();
  w.show();
  int ret = a.exec();
  return ret;
}
