#include "mixr_cover_jpeg.hpp"

#include "esp_heap_caps.h"
#include "esp_log.h"
#include "protocol.h"
#include "src/libs/tjpgd/tjpgd.h"

#include <cstring>

static const char *TAG = "mixr_jpeg";

namespace {

struct DecodeCtx {
    const uint8_t *src;
    size_t src_len;
    size_t pos;
    uint8_t *out;
    int off_x; /* Verschiebung Bild → Ziel (zentrieren) */
    int off_y;
};

/* TJpgDec: Eingabe-Callback. buf == nullptr → nur überspringen. */
size_t jpeg_in(JDEC *jd, uint8_t *buf, size_t len)
{
    auto *ctx = static_cast<DecodeCtx *>(jd->device);
    size_t remain = ctx->src_len - ctx->pos;
    if (len > remain) {
        len = remain;
    }
    if (buf != nullptr && len > 0) {
        memcpy(buf, ctx->src + ctx->pos, len);
    }
    ctx->pos += len;
    return len;
}

/* TJpgDec (ChaN): bei JD_FORMAT 0 ist die Reihenfolge B,G,R — nicht R,G,B. */
int jpeg_out(JDEC *jd, void *bitmap, JRECT *rect)
{
    auto *ctx = static_cast<DecodeCtx *>(jd->device);
    const uint8_t *px = static_cast<const uint8_t *>(bitmap);
    int w = rect->right - rect->left + 1;

    for (int y = rect->top; y <= rect->bottom; y++) {
        int ty = y + ctx->off_y;
        if (ty < 0 || ty >= MIXR_COVER_H) {
            px += w * 3;
            continue;
        }
        for (int x = rect->left; x <= rect->right; x++, px += 3) {
            int tx = x + ctx->off_x;
            if (tx < 0 || tx >= MIXR_COVER_W) {
                continue;
            }
            const uint8_t b = px[0];
            const uint8_t g = px[1];
            const uint8_t r = px[2];
            uint16_t rgb565 = (uint16_t)(((r & 0xF8) << 8) | ((g & 0xFC) << 3) | (b >> 3));
            size_t o = ((size_t)ty * MIXR_COVER_W + (size_t)tx) * 2;
            ctx->out[o] = (uint8_t)(rgb565 & 0xFF);
            ctx->out[o + 1] = (uint8_t)(rgb565 >> 8);
        }
    }
    return 1;
}

} // namespace

bool mixr_cover_decode_jpeg(const uint8_t *jpeg, size_t jpeg_len, uint8_t *out_rgb565)
{
    if (jpeg == nullptr || out_rgb565 == nullptr || jpeg_len < 4) {
        return false;
    }

    /* TJpgDec braucht ~3,5 KB Arbeitsspeicher, mit Huffman-Tabellen und JD_FASTDECODE mehr — 16 KB aus PSRAM. */
    const size_t work_size = 16 * 1024;
    void *work = heap_caps_malloc(work_size, MALLOC_CAP_SPIRAM | MALLOC_CAP_8BIT);
    if (work == nullptr) {
        work = heap_caps_malloc(work_size, MALLOC_CAP_8BIT);
    }
    if (work == nullptr) {
        ESP_LOGE(TAG, "kein Speicher für JPEG-Decoder");
        return false;
    }

    DecodeCtx ctx = {jpeg, jpeg_len, 0, out_rgb565, 0, 0};
    JDEC jd;
    JRESULT r = jd_prepare(&jd, jpeg_in, work, work_size, &ctx);
    if (r != JDR_OK) {
        ESP_LOGW(TAG, "jd_prepare: %d", (int)r);
        heap_caps_free(work);
        return false;
    }

    if (jd.width != MIXR_COVER_W || jd.height != MIXR_COVER_H) {
        ESP_LOGW(TAG, "JPEG %ux%u statt %ux%u — wird zentriert", (unsigned)jd.width, (unsigned)jd.height,
                 (unsigned)MIXR_COVER_W, (unsigned)MIXR_COVER_H);
        memset(out_rgb565, 0, MIXR_COVER_RGB565_BYTES);
        ctx.off_x = ((int)MIXR_COVER_W - (int)jd.width) / 2;
        ctx.off_y = ((int)MIXR_COVER_H - (int)jd.height) / 2;
    }

    r = jd_decomp(&jd, jpeg_out, 0);
    heap_caps_free(work);
    if (r != JDR_OK) {
        ESP_LOGW(TAG, "jd_decomp: %d", (int)r);
        return false;
    }
    return true;
}
