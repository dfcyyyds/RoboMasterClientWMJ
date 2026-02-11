cmake_minimum_required(VERSION 3.10)
project(MockServerCpp)

set(CMAKE_CXX_STANDARD 17)

find_package(Protobuf REQUIRED)
find_package(PahoMqttCpp REQUIRED)

include_directories(${Protobuf_INCLUDE_DIRS})
include_directories(${CMAKE_CURRENT_SOURCE_DIR}/src)

add_executable(mock_server
    src/main.cpp
    src/protocol.cpp
    src/game_simulator.cpp
    src/video_sender.cpp
    src/RoboMasterClientMessage.pb.cc
)

# 若有proto生成的文件请加入
# target_sources(mock_server PRIVATE src/RoboMasterClientMessage.pb.cc)

target_link_libraries(mock_server
    PahoMqttCpp::paho-mqttpp3
    ${Protobuf_LIBRARIES}
)
