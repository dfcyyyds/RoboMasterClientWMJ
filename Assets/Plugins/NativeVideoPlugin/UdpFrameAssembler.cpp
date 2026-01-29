#include "UdpFrameAssembler.h"

#include <algorithm>
#include <cstring>

UdpFrameAssembler::UdpFrameAssembler(size_t maxFrames, size_t maxSliceSize)
    : maxFrames_(maxFrames), maxSliceSize_(maxSliceSize) {}

UdpHeader UdpFrameAssembler::ParseHeader(const uint8_t* data) {
  UdpHeader h;
  h.frameId = data[0] | (data[1] << 8);
  h.sliceId = data[2] | (data[3] << 8);
  h.frameLen = data[4] | (data[5] << 8) | (data[6] << 16) | (data[7] << 24);
  return h;
}

bool UdpFrameAssembler::ProcessPacket(const uint8_t* data, size_t len) {
  if (len < 8) return false;

  UdpHeader hdr = ParseHeader(data);
  const uint8_t* payload = data + 8;
  size_t payloadLen = len - 8;

  std::lock_guard<std::mutex> lock(mtx_);

  FrameBuffer* fb = GetOrCreateFrame(hdr.frameId);
  if (!fb) return false;

  // Initialize frame buffer on first slice
  if (fb->totalLen == 0) {
    fb->frameId = hdr.frameId;
    fb->totalLen = hdr.frameLen;
    fb->expectedSlices = (hdr.frameLen + maxSliceSize_ - 1) / maxSliceSize_;
    fb->slices.resize(fb->expectedSlices);
    fb->lastUpdate = std::chrono::steady_clock::now();
  }

  // Validate slice
  if (hdr.sliceId >= fb->expectedSlices) return false;
  if (fb->slices[hdr.sliceId].received) return false;  // Duplicate

  // Store slice
  fb->slices[hdr.sliceId].data.assign(payload, payload + payloadLen);
  fb->slices[hdr.sliceId].received = true;
  fb->receivedSlices++;
  fb->lastUpdate = std::chrono::steady_clock::now();

  // Check if complete
  if (fb->receivedSlices == fb->expectedSlices) {
    fb->complete = true;
    lastCompleteFrameId_ = hdr.frameId;
    return true;
  }

  return false;
}

bool UdpFrameAssembler::GetCompleteFrame(std::vector<uint8_t>& outFrame,
                                         uint16_t& outFrameId) {
  std::lock_guard<std::mutex> lock(mtx_);

  auto it = frames_.find(lastCompleteFrameId_);
  if (it == frames_.end() || !it->second.complete) {
    return false;
  }

  FrameBuffer& fb = it->second;
  outFrameId = fb.frameId;

  // Assemble slices into single buffer
  outFrame.clear();
  outFrame.reserve(fb.totalLen);

  for (const auto& slice : fb.slices) {
    if (!slice.received) {
      return false;  // Shouldn't happen
    }
    outFrame.insert(outFrame.end(), slice.data.begin(), slice.data.end());
  }

  // Remove assembled frame
  frames_.erase(it);

  return true;
}

void UdpFrameAssembler::EvictStaleFrames(int timeoutMs) {
  std::lock_guard<std::mutex> lock(mtx_);

  auto now = std::chrono::steady_clock::now();
  auto timeout = std::chrono::milliseconds(timeoutMs);

  for (auto it = frames_.begin(); it != frames_.end();) {
    if (!it->second.complete && (now - it->second.lastUpdate) > timeout) {
      it = frames_.erase(it);
    } else {
      ++it;
    }
  }
}

FrameBuffer* UdpFrameAssembler::GetOrCreateFrame(uint16_t frameId) {
  auto it = frames_.find(frameId);
  if (it != frames_.end()) {
    return &it->second;
  }

  // Evict oldest if full
  if (frames_.size() >= maxFrames_) {
    EvictOldestFrame();
  }

  // Create new frame
  FrameBuffer fb;
  fb.frameId = frameId;
  auto result = frames_.emplace(frameId, fb);
  return &result.first->second;
}

void UdpFrameAssembler::EvictOldestFrame() {
  if (frames_.empty()) return;

  auto oldest = frames_.begin();
  auto oldestTime = oldest->second.lastUpdate;

  for (auto it = frames_.begin(); it != frames_.end(); ++it) {
    if (it->second.lastUpdate < oldestTime) {
      oldest = it;
      oldestTime = it->second.lastUpdate;
    }
  }

  frames_.erase(oldest);
}
