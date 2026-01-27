#ifndef MOCKSERVERCPP_LOG_MONITOR_H_
#define MOCKSERVERCPP_LOG_MONITOR_H_
#include <sys/stat.h>

#include <fstream>
#include <string>

class LogMonitor {
 public:
  LogMonitor(const std::string& path, size_t max_size = 10 * 1024 * 1024)
      : log_path_(path), max_size_(max_size) {}

  // 检查并清理日志文件
  void CheckAndTruncate() {
    struct stat st;
    if (stat(log_path_.c_str(), &st) == 0) {
      if (static_cast<size_t>(st.st_size) >= max_size_) {
        std::ofstream ofs(log_path_, std::ios::trunc);
        ofs.close();
      }
    }
  }

 private:
  std::string log_path_;
  size_t max_size_;
};

#endif  // MOCKSERVERCPP_LOG_MONITOR_H_
