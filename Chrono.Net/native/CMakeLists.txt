cmake_minimum_required(VERSION 3.18)
cmake_policy(SET CMP0091 NEW)
project(chrono_native LANGUAGES CXX)

# Chrono.Net native shim: a flat C ABI over Project Chrono, consumed via P/Invoke by src/Chrono.Net.
# Made by Gravicode Studios, led by Kang Fadhil.

option(CHRONO_NET_VEHICLE "Build Chrono::Vehicle bindings" ON)

# Core only: requesting the Vehicle component would also require ChronoModels_vehicle, which we do not build
# (the exposed models are compiled into this shim). Chrono::Chrono_vehicle is linked explicitly below.
find_package(Chrono CONFIG)
if(NOT Chrono_FOUND)
  message(FATAL_ERROR "Chrono not found. Run build/build-native.ps1 or set Chrono_DIR.")
endif()
if(CHRONO_NET_VEHICLE AND NOT TARGET Chrono::Chrono_vehicle)
  message(FATAL_ERROR "Chrono was built without the Vehicle module (CH_ENABLE_MODULE_VEHICLE=ON).")
endif()

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

set(SOURCES src/chrono_native.cpp)
if(CHRONO_NET_VEHICLE)
  # Only the exposed vehicle models are compiled (straight from the Chrono source tree) instead of the
  # whole Chrono_models_vehicle library, which roughly halves the vehicle build time.
  set(CHRONO_SOURCE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../external/chrono" CACHE PATH "Project Chrono source tree")
  set(MODELS_DIR "${CHRONO_SOURCE_DIR}/src/chrono_models/vehicle")
  set(CHRONO_NET_MODELS hmmwv sedan gator uaz citybus m113)
  set(MODEL_SOURCES "")
  foreach(model ${CHRONO_NET_MODELS})
    file(GLOB_RECURSE files "${MODELS_DIR}/${model}/*.cpp")
    list(APPEND MODEL_SOURCES ${files})
  endforeach()
  list(APPEND SOURCES src/chrono_native_vehicle.cpp ${MODEL_SOURCES})
endif()

add_library(chrono_native SHARED ${SOURCES})
target_include_directories(chrono_native PRIVATE include src)
target_link_libraries(chrono_native PRIVATE ${CHRONO_TARGETS})
if(CHRONO_NET_VEHICLE)
  target_link_libraries(chrono_native PRIVATE Chrono::Chrono_vehicle)
  target_include_directories(chrono_native PRIVATE "${CHRONO_SOURCE_DIR}/src")
  target_compile_definitions(chrono_native PRIVATE CHRONO_NET_VEHICLE CH_API_COMPILE_MODELS)
endif()
if(MSVC)
  set_target_properties(chrono_native PROPERTIES MSVC_RUNTIME_LIBRARY ${CHRONO_MSVC_RUNTIME_LIBRARY})
  target_compile_options(chrono_native PRIVATE /bigobj /W3 /wd4251 /wd4275)
endif()
