cmake_minimum_required(VERSION 3.24)
project(StingPlanscapeAddon CXX)
set(CMAKE_CXX_STANDARD 17)

# ── Graphisoft ArchiCAD API location ─────────────────────────────────────────
# Set ARCHICAD_API_DEVKIT to the root of the unpacked DevKit from
# https://archicadapi.graphisoft.com
if(NOT DEFINED ARCHICAD_API_DEVKIT)
    set(ARCHICAD_API_DEVKIT "$ENV{ARCHICAD_API_DEVKIT}" CACHE PATH "ArchiCAD DevKit root")
endif()

if(NOT EXISTS "${ARCHICAD_API_DEVKIT}/Support/Inc/ACAPinc.h")
    message(FATAL_ERROR "ArchiCAD DevKit not found at ${ARCHICAD_API_DEVKIT}. "
        "Download from https://archicadapi.graphisoft.com and set ARCHICAD_API_DEVKIT.")
endif()

# ── Source files ──────────────────────────────────────────────────────────────
set(SOURCES
    src/StingPlanscapeAddon.cpp
    src/PlanscapeClient.cpp        # to be created: HTTP implementation
    src/LoginDialog.cpp            # to be created: DG login dialog
    src/SettingsDialog.cpp         # to be created: DG settings dialog
)

# ── Add-On shared library ─────────────────────────────────────────────────────
add_library(StingPlanscapeAddon SHARED ${SOURCES})

target_include_directories(StingPlanscapeAddon PRIVATE
    include/
    "${ARCHICAD_API_DEVKIT}/Support/Inc"
    "${ARCHICAD_API_DEVKIT}/Support/Modules/GSRoot"
    "${ARCHICAD_API_DEVKIT}/Support/Modules/DGLib"
)

# ── Platform-specific HTTP client ─────────────────────────────────────────────
if(WIN32)
    # WinHTTP — ships with Windows SDK, no extra dependency
    target_link_libraries(StingPlanscapeAddon PRIVATE winhttp.lib)
    target_compile_definitions(StingPlanscapeAddon PRIVATE PLANSCAPE_HTTP_WINHTTP)
elseif(APPLE)
    # libcurl ships with macOS
    find_package(CURL REQUIRED)
    target_link_libraries(StingPlanscapeAddon PRIVATE CURL::libcurl)
    target_compile_definitions(StingPlanscapeAddon PRIVATE PLANSCAPE_HTTP_CURL)
endif()

# ── ArchiCAD DevKit libraries ─────────────────────────────────────────────────
target_link_directories(StingPlanscapeAddon PRIVATE
    "${ARCHICAD_API_DEVKIT}/Support/Lib"
)

# Output as .apx (ArchiCAD Add-On extension)
set_target_properties(StingPlanscapeAddon PROPERTIES
    OUTPUT_NAME "StingPlanscape"
    SUFFIX ".apx"
)
