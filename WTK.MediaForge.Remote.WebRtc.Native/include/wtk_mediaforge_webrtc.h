#pragma once
#include <stdint.h>
#ifdef _WIN32
#define MF_WEBRTC_API __declspec(dllexport)
#else
#define MF_WEBRTC_API __attribute__((visibility("default")))
#endif
#ifdef __cplusplus
extern "C" {
#endif
MF_WEBRTC_API int mf_webrtc_abi_version(void);
/* ABI v1 will expose session SDP/ICE, encoded-H264 submit/pop, PLI, and stats. It never accepts raw video frames. */
#ifdef __cplusplus
}
#endif
