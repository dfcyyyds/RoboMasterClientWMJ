#pragma once
#include <chrono>
#include <cstdint>
#include <mutex>
#include <unordered_map>
#include <vector>

// UDP packet header: frameId(u16) + sliceId(u16) + frameLen(u32) = 8 bytes
struct UdpHeader {
  uint16_t frameId;
  uint16_t sliceId;
  uint32_t frameLen;
};

struct FrameSlice {
  std::vector<uint8_t> data;
  bool received = false;
};

struct FrameBuffer {
  uint16_t frameId = 0;
  uint32_t totalLen = 0;
  uint16_t expectedSlices = 0;
  uint16_t receivedSlices = 0;
  std::vector<FrameSlice> slices;
  std::chrono::steady_clock::time_point lastUpdate;
  bool complete = false;
};

class UdpFrameAssembler {
 public:
  UdpFrameAssembler(size_t maxFrames = 16, size_t maxSliceSize = 65536);
  ~UdpFrameAssembler() = default;

  // Process incoming UDP packet with 8-byte header
  // Returns true if a complete frame is available
  bool ProcessPacket(const uint8_t* data, size_t len);

  // Get the assembled complete frame (call after ProcessPacket returns true)
  // Returns frame data and size, clears the frame buffer
  bool GetCompleteFrame(std::vector<uint8_t>& outFrame, uint16_t& outFrameId);

  // Check for stale frames and evict them
  void EvictStaleFrames(int timeoutMs = 1000);

 private:
  UdpHeader ParseHeader(const uint8_t* data);
  FrameBuffer* GetOrCreateFrame(uint16_t frameId);
  void EvictOldestFrame();

  std::unordered_map<uint16_t, FrameBuffer> frames_;
  std::mutex mtx_;
  size_t maxFrames_;
  size_t maxSliceSize_;
  uint16_t lastCompleteFrameId_ = 0;
};
