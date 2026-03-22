#pragma once

#include <stdint.h>

void mixr_app_run(void);

/** Wiedergabe-Befehl an den PC (SMTC): Nutzlast 1 Byte, Werte wie MediaSubCmd in protocol.h */
void mixr_pc_send_media_cmd(uint8_t subcmd);
