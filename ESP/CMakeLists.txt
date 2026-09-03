cmake_minimum_required(VERSION 3.16)

# Firmware-Version (landet in esp_app_desc_t und wird im HELLO an den Host gemeldet):
#   1. ESP/version.txt   — schreibt die Release-Pipeline aus dem Git-Tag (ESP-IDF-Standardmechanismus),
#   2. Umgebungsvariable MIXR_VERSION — für lokale Release-Builds,
#   3. sonst `git describe` (z. B. 256c126-dirty) — Entwicklungsstand.
# App und Firmware eines Releases tragen so dieselbe Nummer.
if(NOT EXISTS "${CMAKE_CURRENT_LIST_DIR}/version.txt"
   AND DEFINED ENV{MIXR_VERSION} AND NOT "$ENV{MIXR_VERSION}" STREQUAL "")
    set(PROJECT_VER "$ENV{MIXR_VERSION}")
endif()

include($ENV{IDF_PATH}/tools/cmake/project.cmake)
project(Mixr)
