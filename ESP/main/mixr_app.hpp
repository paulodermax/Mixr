#pragma once

#include <stdint.h>

void mixr_app_run(void);

/** Wiedergabe-Befehl an den PC (SMTC): Nutzlast 1 Byte, Werte wie MediaSubCmd in protocol.h */
void mixr_pc_send_media_cmd(uint8_t subcmd);

/** Discord/VoIP-Mute am PC (Hotkey), siehe PktType::VOIP_MUTE_CMD */
void mixr_pc_send_voip_mute(void);

/** Discord-Deafen am PC (Hotkey), siehe PktType::VOIP_DEAFEN */
void mixr_pc_send_voip_deafen(void);

/** Bildschirm teilen am PC (Hotkey), siehe PktType::SHARE_SCREEN_CMD */
void mixr_pc_send_share_screen(void);
