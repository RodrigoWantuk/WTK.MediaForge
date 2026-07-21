#include "wtk_mediaforge_webrtc.h"
#include <condition_variable>
#include <cstring>
#include <mutex>
#include <new>
#include <string>

struct mf_webrtc_session {
    std::mutex mutex;
    std::condition_variable callbacks_drained;
    bool destroying = false;
    bool closed = false;
    uint32_t callbacks_in_flight = 0;
    mf_webrtc_packet_callback video_callback = nullptr;
    mf_webrtc_packet_callback audio_callback = nullptr;
    mf_webrtc_keyframe_callback keyframe_callback = nullptr;
    mf_webrtc_state_callback state_callback = nullptr;
    mf_webrtc_ice_candidate_callback ice_callback = nullptr;
    void* video_user = nullptr;
    void* audio_user = nullptr;
    void* keyframe_user = nullptr;
    void* state_user = nullptr;
    void* ice_user = nullptr;
};

static mf_webrtc_result fail(mf_webrtc_error* error, mf_webrtc_result code, const char* message) {
    if (error && error->struct_size >= sizeof(mf_webrtc_error) &&
        error->struct_version == MF_WEBRTC_STRUCT_VERSION_1) {
        error->code = static_cast<int32_t>(code);
        std::strncpy(error->message, message, sizeof(error->message) - 1);
        error->message[sizeof(error->message) - 1] = '\0';
    }
    return code;
}

static mf_webrtc_result validate_session(mf_webrtc_session* session, mf_webrtc_error* error) {
    if (!session) return fail(error, MF_WEBRTC_INVALID_ARGUMENT, "Session is null.");
    std::lock_guard<std::mutex> lock(session->mutex);
    if (session->destroying || session->closed)
        return fail(error, MF_WEBRTC_INVALID_STATE, "Session is closing or closed.");
    return MF_WEBRTC_OK;
}

extern "C" {
uint32_t MF_WEBRTC_CALL mf_webrtc_abi_version(void) { return MF_WEBRTC_ABI_VERSION; }
uint8_t MF_WEBRTC_CALL mf_webrtc_backend_available(void) { return 0; }

mf_webrtc_result MF_WEBRTC_CALL mf_webrtc_session_create(
    const mf_webrtc_session_config* config, mf_webrtc_session** output, mf_webrtc_error* error) {
    if (!config || !output) return fail(error, MF_WEBRTC_INVALID_ARGUMENT, "Config and output are required.");
    *output = nullptr;
    if (config->struct_size < sizeof(mf_webrtc_session_config) ||
        config->struct_version != MF_WEBRTC_STRUCT_VERSION_1 ||
        config->abi_version != MF_WEBRTC_ABI_VERSION)
        return fail(error, MF_WEBRTC_INCOMPATIBLE_ABI, "Session config ABI version or size is incompatible.");
    auto* session = new (std::nothrow) mf_webrtc_session();
    if (!session) return fail(error, MF_WEBRTC_OPERATION_FAILED, "Session allocation failed.");
    *output = session;
    return MF_WEBRTC_OK;
}

mf_webrtc_result MF_WEBRTC_CALL mf_webrtc_session_destroy(
    mf_webrtc_session** value, mf_webrtc_error* error) {
    if (!value || !*value) return MF_WEBRTC_OK;
    mf_webrtc_session* session = *value;
    *value = nullptr;
    {
        std::unique_lock<std::mutex> lock(session->mutex);
        session->destroying = true;
        session->closed = true;
        session->video_callback = nullptr;
        session->audio_callback = nullptr;
        session->keyframe_callback = nullptr;
        session->state_callback = nullptr;
        session->ice_callback = nullptr;
        session->callbacks_drained.wait(lock, [session] { return session->callbacks_in_flight == 0; });
    }
    delete session;
    (void)error;
    return MF_WEBRTC_OK;
}

#define MF_BACKEND_REQUIRED(name) \
    mf_webrtc_result MF_WEBRTC_CALL name(mf_webrtc_session* session, mf_webrtc_error* error) { \
        auto valid = validate_session(session, error); \
        return valid == MF_WEBRTC_OK ? fail(error, MF_WEBRTC_BACKEND_UNAVAILABLE, "Pinned libwebrtc backend was not linked into this ABI build.") : valid; \
    }

mf_webrtc_result MF_WEBRTC_CALL mf_webrtc_session_create_offer(
    mf_webrtc_session* session, char* output, size_t capacity, size_t* required, mf_webrtc_error* error) {
    (void)output; (void)capacity;
    if (required) *required = 0;
    auto valid = validate_session(session, error);
    return valid == MF_WEBRTC_OK ? fail(error, MF_WEBRTC_BACKEND_UNAVAILABLE, "Pinned libwebrtc backend was not linked into this ABI build.") : valid;
}

static mf_webrtc_result backend_string_operation(
    mf_webrtc_session* session, mf_webrtc_string_view value, mf_webrtc_error* error) {
    if (!value.data || value.size == 0) return fail(error, MF_WEBRTC_INVALID_ARGUMENT, "A non-empty UTF-8 value is required.");
    auto valid = validate_session(session, error);
    return valid == MF_WEBRTC_OK ? fail(error, MF_WEBRTC_BACKEND_UNAVAILABLE, "Pinned libwebrtc backend was not linked into this ABI build.") : valid;
}

mf_webrtc_result MF_WEBRTC_CALL mf_webrtc_session_set_local_description(mf_webrtc_session* s, mf_webrtc_string_view type, mf_webrtc_string_view sdp, mf_webrtc_error* e) { (void)type; return backend_string_operation(s, sdp, e); }
mf_webrtc_result MF_WEBRTC_CALL mf_webrtc_session_set_remote_description(mf_webrtc_session* s, mf_webrtc_string_view type, mf_webrtc_string_view sdp, mf_webrtc_error* e) { (void)type; return backend_string_operation(s, sdp, e); }
mf_webrtc_result MF_WEBRTC_CALL mf_webrtc_session_add_ice_candidate(mf_webrtc_session* s, mf_webrtc_string_view candidate, mf_webrtc_string_view mid, int32_t index, mf_webrtc_error* e) { (void)mid; (void)index; return backend_string_operation(s, candidate, e); }
mf_webrtc_result MF_WEBRTC_CALL mf_webrtc_session_add_ice_server(mf_webrtc_session* s, mf_webrtc_string_view urls, mf_webrtc_string_view user, mf_webrtc_string_view credential, mf_webrtc_error* e) { (void)user; (void)credential; return backend_string_operation(s, urls, e); }
MF_BACKEND_REQUIRED(mf_webrtc_session_connect)

mf_webrtc_result MF_WEBRTC_CALL mf_webrtc_session_close(mf_webrtc_session* session, mf_webrtc_error* error) {
    if (!session) return fail(error, MF_WEBRTC_INVALID_ARGUMENT, "Session is null.");
    std::lock_guard<std::mutex> lock(session->mutex);
    session->closed = true;
    return MF_WEBRTC_OK;
}

mf_webrtc_result MF_WEBRTC_CALL mf_webrtc_publisher_send_h264(mf_webrtc_session* s, const mf_webrtc_h264_packet* p, mf_webrtc_error* e) {
    if (!p || p->struct_size < sizeof(*p) || p->struct_version != MF_WEBRTC_STRUCT_VERSION_1 || !p->data || p->size == 0)
        return fail(e, MF_WEBRTC_INVALID_ARGUMENT, "A compatible non-empty H.264 packet is required.");
    auto valid = validate_session(s, e);
    return valid == MF_WEBRTC_OK ? fail(e, MF_WEBRTC_BACKEND_UNAVAILABLE, "Pinned libwebrtc backend was not linked into this ABI build.") : valid;
}
mf_webrtc_result MF_WEBRTC_CALL mf_webrtc_publisher_send_audio(mf_webrtc_session* s, const mf_webrtc_audio_packet* p, mf_webrtc_error* e) {
    if (!p || p->struct_size < sizeof(*p) || p->struct_version != MF_WEBRTC_STRUCT_VERSION_1 || !p->data || p->size == 0)
        return fail(e, MF_WEBRTC_INVALID_ARGUMENT, "A compatible non-empty audio packet is required.");
    auto valid = validate_session(s, e);
    return valid == MF_WEBRTC_OK ? fail(e, MF_WEBRTC_BACKEND_UNAVAILABLE, "Pinned libwebrtc backend was not linked into this ABI build.") : valid;
}

#define MF_SET_CALLBACK(name, field, user_field, callback_type) \
    mf_webrtc_result MF_WEBRTC_CALL name(mf_webrtc_session* session, callback_type callback, void* user, mf_webrtc_error* error) { \
        auto valid = validate_session(session, error); if (valid != MF_WEBRTC_OK) return valid; \
        std::lock_guard<std::mutex> lock(session->mutex); session->field = callback; session->user_field = user; return MF_WEBRTC_OK; \
    }
MF_SET_CALLBACK(mf_webrtc_session_set_video_packet_callback, video_callback, video_user, mf_webrtc_packet_callback)
MF_SET_CALLBACK(mf_webrtc_session_set_audio_packet_callback, audio_callback, audio_user, mf_webrtc_packet_callback)
MF_SET_CALLBACK(mf_webrtc_session_set_keyframe_request_callback, keyframe_callback, keyframe_user, mf_webrtc_keyframe_callback)
MF_SET_CALLBACK(mf_webrtc_session_set_state_callback, state_callback, state_user, mf_webrtc_state_callback)
MF_SET_CALLBACK(mf_webrtc_session_set_ice_candidate_callback, ice_callback, ice_user, mf_webrtc_ice_candidate_callback)

mf_webrtc_result MF_WEBRTC_CALL mf_webrtc_session_get_selected_candidate(mf_webrtc_session* s, mf_webrtc_selected_candidate* c, mf_webrtc_error* e) {
    if (!c || c->struct_size < sizeof(*c) || c->struct_version != MF_WEBRTC_STRUCT_VERSION_1)
        return fail(e, MF_WEBRTC_INCOMPATIBLE_ABI, "Candidate struct version or size is incompatible.");
    auto valid = validate_session(s, e);
    return valid == MF_WEBRTC_OK ? fail(e, MF_WEBRTC_BACKEND_UNAVAILABLE, "Pinned libwebrtc backend was not linked into this ABI build.") : valid;
}
mf_webrtc_result MF_WEBRTC_CALL mf_webrtc_session_get_stats(mf_webrtc_session* s, mf_webrtc_stats* stats, mf_webrtc_error* e) {
    if (!stats || stats->struct_size < sizeof(*stats) || stats->struct_version != MF_WEBRTC_STRUCT_VERSION_1)
        return fail(e, MF_WEBRTC_INCOMPATIBLE_ABI, "Stats struct version or size is incompatible.");
    auto valid = validate_session(s, e);
    return valid == MF_WEBRTC_OK ? fail(e, MF_WEBRTC_BACKEND_UNAVAILABLE, "Pinned libwebrtc backend was not linked into this ABI build.") : valid;
}
}
