# Erzwingt partitions.csv (2 MiB factory) auch wenn sdkconfig noch "single_app" 1 MiB hat.
# Muss nach dem project_include von partition_table laufen → Komponentenname sortiert danach (z_…).
if(EXISTS "${CMAKE_SOURCE_DIR}/partitions.csv")
    get_filename_component(_mixr_partitions_csv "${CMAKE_SOURCE_DIR}/partitions.csv" ABSOLUTE)
    set(PARTITION_CSV_PATH "${_mixr_partitions_csv}")
    message(STATUS "Mixr: PARTITION_CSV_PATH -> ${PARTITION_CSV_PATH}")
endif()
