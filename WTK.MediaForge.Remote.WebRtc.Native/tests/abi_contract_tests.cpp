#include "wtk_mediaforge_webrtc.h"
#include <cassert>
#include <cstring>

int main() {
    assert(mf_webrtc_abi_version() == MF_WEBRTC_ABI_VERSION);
    assert(mf_webrtc_backend_available() == 0);

    mf_webrtc_error error{};
    error.struct_size = sizeof(error);
    error.struct_version = MF_WEBRTC_STRUCT_VERSION_1;
    mf_webrtc_session_config config{};
    config.struct_size = sizeof(config);
    config.struct_version = MF_WEBRTC_STRUCT_VERSION_1;
    config.abi_version = MF_WEBRTC_ABI_VERSION;

    for (int i = 0; i < 1000; ++i) {
        mf_webrtc_session* session = nullptr;
        assert(mf_webrtc_session_create(&config, &session, &error) == MF_WEBRTC_OK);
        assert(session != nullptr);
        assert(mf_webrtc_session_destroy(&session, &error) == MF_WEBRTC_OK);
        assert(session == nullptr);
        assert(mf_webrtc_session_destroy(&session, &error) == MF_WEBRTC_OK);
    }

    config.abi_version = MF_WEBRTC_ABI_VERSION + 1;
    mf_webrtc_session* incompatible = nullptr;
    assert(mf_webrtc_session_create(&config, &incompatible, &error) == MF_WEBRTC_INCOMPATIBLE_ABI);
    assert(incompatible == nullptr);
    assert(std::strlen(error.message) > 0);
    return 0;
}

