#pragma once
#include <stddef.h>
#include <stdint.h>

#ifdef _WIN32
#define MF_WEBRTC_API __declspec(dllexport)
#define MF_WEBRTC_CALL __cdecl
#else
#define MF_WEBRTC_API __attribute__((visibility("default")))
#define MF_WEBRTC_CALL
#endif

#ifdef __cplusplus
extern "C" {
#endif

#define MF_WEBRTC_ABI_VERSION 2u
#define MF_WEBRTC_STRUCT_VERSION_1 1u

typedef struct mf_webrtc_session mf_webrtc_session;

typedef enum mf_webrtc_result {
    MF_WEBRTC_OK = 0,
    MF_WEBRTC_INVALID_ARGUMENT = 1,
    MF_WEBRTC_INCOMPATIBLE_ABI = 2,
    MF_WEBRTC_INVALID_STATE = 3,
    MF_WEBRTC_BACKEND_UNAVAILABLE = 4,
    MF_WEBRTC_OPERATION_FAILED = 5,
    MF_WEBRTC_BUFFER_TOO_SMALL = 6
} mf_webrtc_result;

typedef enum mf_webrtc_session_state {
    MF_WEBRTC_STATE_NEW = 0,
    MF_WEBRTC_STATE_CONNECTING = 1,
    MF_WEBRTC_STATE_CONNECTED = 2,
    MF_WEBRTC_STATE_CLOSED = 3,
    MF_WEBRTC_STATE_FAILED = 4
} mf_webrtc_session_state;

typedef struct mf_webrtc_string_view {
    const char* data;
    size_t size;
} mf_webrtc_string_view;

typedef struct mf_webrtc_error {
    uint32_t struct_size;
    uint32_t struct_version;
    int32_t code;
    char message[512];
} mf_webrtc_error;

typedef struct mf_webrtc_session_config {
    uint32_t struct_size;
    uint32_t struct_version;
    uint32_t abi_version;
    uint8_t enable_audio;
    uint8_t reserved[7];
} mf_webrtc_session_config;

typedef struct mf_webrtc_h264_packet {
    uint32_t struct_size;
    uint32_t struct_version;
    const uint8_t* data;
    size_t size;
    int64_t presentation_time_us;
    int64_t duration_us;
    uint8_t is_key_frame;
    uint8_t reserved[7];
} mf_webrtc_h264_packet;

typedef struct mf_webrtc_audio_packet {
    uint32_t struct_size;
    uint32_t struct_version;
    const uint8_t* data;
    size_t size;
    int64_t presentation_time_us;
    int64_t duration_us;
    uint32_t codec;
} mf_webrtc_audio_packet;

typedef struct mf_webrtc_selected_candidate {
    uint32_t struct_size;
    uint32_t struct_version;
    uint8_t is_relay;
    uint8_t reserved[7];
    char local_type[32];
    char remote_type[32];
    char protocol[16];
    char remote_address[128];
} mf_webrtc_selected_candidate;

typedef struct mf_webrtc_stats {
    uint32_t struct_size;
    uint32_t struct_version;
    uint64_t bytes_sent;
    uint64_t bytes_received;
    uint64_t packets_lost;
    uint64_t frames_sent;
    uint64_t frames_received;
    uint64_t keyframe_requests;
    uint64_t round_trip_time_us;
    uint64_t jitter_us;
    uint64_t available_outgoing_bitrate_bps;
} mf_webrtc_stats;

/* Callback buffers are borrowed and valid only for the callback duration.
   Callbacks run on libwebrtc signaling/network/worker threads and may overlap.
   The user context remains caller-owned until destroy returns. No callback begins
   after destroy starts, and destroy waits for callbacks already in flight. */
typedef void (MF_WEBRTC_CALL *mf_webrtc_packet_callback)(void* user, const uint8_t* data, size_t size, int64_t pts_us, int64_t duration_us, uint8_t key_frame);
typedef void (MF_WEBRTC_CALL *mf_webrtc_keyframe_callback)(void* user);
typedef void (MF_WEBRTC_CALL *mf_webrtc_state_callback)(void* user, mf_webrtc_session_state state, mf_webrtc_string_view detail);
typedef void (MF_WEBRTC_CALL *mf_webrtc_ice_candidate_callback)(void* user, mf_webrtc_string_view candidate, mf_webrtc_string_view sdp_mid, int32_t sdp_mline_index);

MF_WEBRTC_API uint32_t MF_WEBRTC_CALL mf_webrtc_abi_version(void);
MF_WEBRTC_API uint8_t MF_WEBRTC_CALL mf_webrtc_backend_available(void);
MF_WEBRTC_API mf_webrtc_result MF_WEBRTC_CALL mf_webrtc_session_create(const mf_webrtc_session_config* config, mf_webrtc_session** session, mf_webrtc_error* error);
MF_WEBRTC_API mf_webrtc_result MF_WEBRTC_CALL mf_webrtc_session_destroy(mf_webrtc_session** session, mf_webrtc_error* error);
MF_WEBRTC_API mf_webrtc_result MF_WEBRTC_CALL mf_webrtc_session_create_offer(mf_webrtc_session* session, char* output, size_t capacity, size_t* required, mf_webrtc_error* error);
MF_WEBRTC_API mf_webrtc_result MF_WEBRTC_CALL mf_webrtc_session_set_local_description(mf_webrtc_session* session, mf_webrtc_string_view type, mf_webrtc_string_view sdp, mf_webrtc_error* error);
MF_WEBRTC_API mf_webrtc_result MF_WEBRTC_CALL mf_webrtc_session_set_remote_description(mf_webrtc_session* session, mf_webrtc_string_view type, mf_webrtc_string_view sdp, mf_webrtc_error* error);
MF_WEBRTC_API mf_webrtc_result MF_WEBRTC_CALL mf_webrtc_session_add_ice_candidate(mf_webrtc_session* session, mf_webrtc_string_view candidate, mf_webrtc_string_view sdp_mid, int32_t sdp_mline_index, mf_webrtc_error* error);
MF_WEBRTC_API mf_webrtc_result MF_WEBRTC_CALL mf_webrtc_session_add_ice_server(mf_webrtc_session* session, mf_webrtc_string_view urls_json, mf_webrtc_string_view username, mf_webrtc_string_view credential, mf_webrtc_error* error);
MF_WEBRTC_API mf_webrtc_result MF_WEBRTC_CALL mf_webrtc_session_connect(mf_webrtc_session* session, mf_webrtc_error* error);
MF_WEBRTC_API mf_webrtc_result MF_WEBRTC_CALL mf_webrtc_session_close(mf_webrtc_session* session, mf_webrtc_error* error);
MF_WEBRTC_API mf_webrtc_result MF_WEBRTC_CALL mf_webrtc_publisher_send_h264(mf_webrtc_session* session, const mf_webrtc_h264_packet* packet, mf_webrtc_error* error);
MF_WEBRTC_API mf_webrtc_result MF_WEBRTC_CALL mf_webrtc_publisher_send_audio(mf_webrtc_session* session, const mf_webrtc_audio_packet* packet, mf_webrtc_error* error);
MF_WEBRTC_API mf_webrtc_result MF_WEBRTC_CALL mf_webrtc_session_set_video_packet_callback(mf_webrtc_session* session, mf_webrtc_packet_callback callback, void* user, mf_webrtc_error* error);
MF_WEBRTC_API mf_webrtc_result MF_WEBRTC_CALL mf_webrtc_session_set_audio_packet_callback(mf_webrtc_session* session, mf_webrtc_packet_callback callback, void* user, mf_webrtc_error* error);
MF_WEBRTC_API mf_webrtc_result MF_WEBRTC_CALL mf_webrtc_session_set_keyframe_request_callback(mf_webrtc_session* session, mf_webrtc_keyframe_callback callback, void* user, mf_webrtc_error* error);
MF_WEBRTC_API mf_webrtc_result MF_WEBRTC_CALL mf_webrtc_session_set_state_callback(mf_webrtc_session* session, mf_webrtc_state_callback callback, void* user, mf_webrtc_error* error);
MF_WEBRTC_API mf_webrtc_result MF_WEBRTC_CALL mf_webrtc_session_set_ice_candidate_callback(mf_webrtc_session* session, mf_webrtc_ice_candidate_callback callback, void* user, mf_webrtc_error* error);
MF_WEBRTC_API mf_webrtc_result MF_WEBRTC_CALL mf_webrtc_session_get_selected_candidate(mf_webrtc_session* session, mf_webrtc_selected_candidate* candidate, mf_webrtc_error* error);
MF_WEBRTC_API mf_webrtc_result MF_WEBRTC_CALL mf_webrtc_session_get_stats(mf_webrtc_session* session, mf_webrtc_stats* stats, mf_webrtc_error* error);

#ifdef __cplusplus
}
#endif
