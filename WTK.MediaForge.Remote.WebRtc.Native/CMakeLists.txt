cmake_minimum_required(VERSION 3.24)
project(wtk_mediaforge_webrtc LANGUAGES CXX)
# libwebrtc revision and the encoded-access-unit bridge patch are intentionally supplied by the reproducible native build pipeline.
add_library(wtk_mediaforge_webrtc SHARED src/wtk_mediaforge_webrtc.cpp)
target_include_directories(wtk_mediaforge_webrtc PUBLIC include)
target_compile_features(wtk_mediaforge_webrtc PRIVATE cxx_std_20)
