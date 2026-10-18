cmake_minimum_required(VERSION 3.24)
project(SmitePatchNv LANGUAGES CXX)

include(FetchContent)

# Same pinned NVAPI snapshot as native/Smite2NvApi. build.ps1 points
# FETCHCONTENT_SOURCE_DIR_NVAPI at the existing checkout when it is present.
FetchContent_Declare(
    nvapi
    GIT_REPOSITORY https://github.com/NVIDIA/nvapi.git
    GIT_TAG 87dca625e83fd89a983e19b904e5f3a580da90d2
    GIT_SHALLOW FALSE
)
FetchContent_MakeAvailable(nvapi)

add_library(SmitePatchNv SHARED SmitePatchNv.cpp)
target_compile_features(SmitePatchNv PRIVATE cxx_std_20)
target_compile_definitions(SmitePatchNv PRIVATE UNICODE _UNICODE)
target_include_directories(SmitePatchNv PRIVATE "${nvapi_SOURCE_DIR}")
target_link_libraries(SmitePatchNv PRIVATE "${nvapi_SOURCE_DIR}/amd64/nvapi64.lib")

if(MSVC)
    set_property(TARGET SmitePatchNv PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
    target_compile_options(SmitePatchNv PRIVATE /W4 /WX /utf-8 /permissive-)
endif()

install(TARGETS SmitePatchNv RUNTIME DESTINATION .)
install(FILES "${nvapi_SOURCE_DIR}/License.txt" DESTINATION licenses RENAME NVIDIA-NVAPI.txt)
