cmake_minimum_required(VERSION 3.16)
project(MockServerQtGui)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_AUTOMOC ON)
set(CMAKE_AUTORCC ON)
set(CMAKE_AUTOUIC ON)

find_package(Qt6 REQUIRED COMPONENTS Widgets)

include_directories(
    ../src
    ../
)

add_executable(MockServerQtGui
    main_with_thread.cpp
    mainwindow.cpp
    mainwindow.h
    worker.cpp
)

target_link_libraries(MockServerQtGui PRIVATE Qt6::Widgets)
