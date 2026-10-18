// Generic NVIDIA driver-profile access for the SMITE 2 patcher.
// The managed side decides which settings to write; this layer only reads,
// writes or deletes DWORD settings on the profile that owns one executable.

#include <Windows.h>
#include <nvapi.h>

#include <cstdint>
#include <cwchar>
#include <string>

#define SMITEPATCH_API extern "C" __declspec(dllexport)

namespace
{
    // Used only when the driver has no profile for the executable yet.
    constexpr wchar_t FallbackProfileName[] = L"SMITE 2 Patcher";

    void CopyText(wchar_t* destination, const std::uint32_t length, const std::wstring& source)
    {
        if (destination != nullptr && length != 0)
        {
            wcsncpy_s(destination, length, source.c_str(), _TRUNCATE);
        }
    }

    void ToNvText(NvAPI_UnicodeString destination, const wchar_t* source)
    {
        std::size_t index = 0;
        for (; source != nullptr && index + 1 < NVAPI_UNICODE_STRING_MAX && source[index] != L'\0'; ++index)
        {
            destination[index] = static_cast<NvU16>(source[index]);
        }
        destination[index] = 0;
    }

    std::wstring FromNvText(const NvU16* source)
    {
        std::wstring result;
        for (std::size_t index = 0; source != nullptr && index < NVAPI_UNICODE_STRING_MAX && source[index] != 0; ++index)
        {
            result.push_back(static_cast<wchar_t>(source[index]));
        }
        return result;
    }

    std::wstring StatusText(const NvAPI_Status status)
    {
        NvAPI_ShortString message{};
        if (NvAPI_GetErrorMessage(status, message) != NVAPI_OK)
        {
            return L"NVAPI error " + std::to_wstring(static_cast<int>(status));
        }
        std::wstring result;
        for (const char* character = message; *character != '\0'; ++character)
        {
            result.push_back(static_cast<wchar_t>(static_cast<unsigned char>(*character)));
        }
        return result;
    }

    int Fail(const NvAPI_Status status, const wchar_t* operation, wchar_t* error, const std::uint32_t errorLength)
    {
        CopyText(error, errorLength, std::wstring(operation) + L": " + StatusText(status));
        return static_cast<int>(status == NVAPI_OK ? NVAPI_ERROR : status);
    }

    class DrsSession final
    {
    public:
        NvAPI_Status Open()
        {
            NvAPI_Status result = NvAPI_Initialize();
            if (result != NVAPI_OK)
            {
                return result;
            }
            initialized_ = true;
            result = NvAPI_DRS_CreateSession(&handle_);
            if (result != NVAPI_OK)
            {
                return result;
            }
            return NvAPI_DRS_LoadSettings(handle_);
        }

        ~DrsSession()
        {
            if (handle_ != nullptr)
            {
                NvAPI_DRS_DestroySession(handle_);
            }
            if (initialized_)
            {
                NvAPI_Unload();
            }
        }

        DrsSession() = default;
        DrsSession(const DrsSession&) = delete;
        DrsSession& operator=(const DrsSession&) = delete;

        NvDRSSessionHandle Handle() const noexcept { return handle_; }

    private:
        bool initialized_ = false;
        NvDRSSessionHandle handle_ = nullptr;
    };

    NvAPI_Status FindProfile(const NvDRSSessionHandle session, const wchar_t* executable, NvDRSProfileHandle* profile)
    {
        NVDRS_APPLICATION application{};
        application.version = NVDRS_APPLICATION_VER;
        NvAPI_UnicodeString name{};
        ToNvText(name, executable);
        return NvAPI_DRS_FindApplicationByName(session, name, profile, &application);
    }

    NvAPI_Status EnsureProfile(const NvDRSSessionHandle session, const wchar_t* executable, NvDRSProfileHandle* profile)
    {
        NvAPI_Status result = FindProfile(session, executable, profile);
        if (result == NVAPI_OK)
        {
            return result;
        }

        NVDRS_PROFILE info{};
        info.version = NVDRS_PROFILE_VER;
        ToNvText(info.profileName, FallbackProfileName);
        result = NvAPI_DRS_FindProfileByName(session, info.profileName, profile);
        if (result != NVAPI_OK)
        {
            result = NvAPI_DRS_CreateProfile(session, &info, profile);
            if (result != NVAPI_OK)
            {
                return result;
            }
        }

        NVDRS_APPLICATION application{};
        application.version = NVDRS_APPLICATION_VER;
        ToNvText(application.appName, executable);
        ToNvText(application.userFriendlyName, executable);
        result = NvAPI_DRS_CreateApplication(session, *profile, &application);
        if (result == NVAPI_EXECUTABLE_ALREADY_IN_USE)
        {
            return FindProfile(session, executable, profile);
        }
        return result;
    }

    std::wstring ProfileName(const NvDRSSessionHandle session, const NvDRSProfileHandle profile)
    {
        NVDRS_PROFILE info{};
        info.version = NVDRS_PROFILE_VER;
        return NvAPI_DRS_GetProfileInfo(session, profile, &info) == NVAPI_OK ? FromNvText(info.profileName) : std::wstring();
    }
}

SMITEPATCH_API int __cdecl SmitePatchNv_DriverVersion(std::uint32_t* version, wchar_t* error, const std::uint32_t errorLength)
{
    if (version == nullptr)
    {
        return Fail(NVAPI_INVALID_ARGUMENT, L"Driver version", error, errorLength);
    }
    *version = 0;
    NvAPI_Status result = NvAPI_Initialize();
    if (result != NVAPI_OK)
    {
        return Fail(result, L"NVAPI", error, errorLength);
    }
    NvAPI_ShortString branch{};
    NvU32 driver = 0;
    result = NvAPI_SYS_GetDriverAndBranchVersion(&driver, branch);
    NvAPI_Unload();
    if (result != NVAPI_OK)
    {
        return Fail(result, L"Driver version", error, errorLength);
    }
    *version = driver;
    CopyText(error, errorLength, L"");
    return NVAPI_OK;
}

// Reads settings without creating anything. existed[i] is 1 only when the
// value is stored on this executable's profile (not inherited from global).
SMITEPATCH_API int __cdecl SmitePatchNv_Read(
    const wchar_t* executable,
    const std::uint32_t* ids,
    const std::uint32_t count,
    std::uint32_t* existed,
    std::uint32_t* values,
    wchar_t* profileName,
    const std::uint32_t profileNameLength,
    wchar_t* error,
    const std::uint32_t errorLength)
{
    if (executable == nullptr || ids == nullptr || existed == nullptr || values == nullptr)
    {
        return Fail(NVAPI_INVALID_ARGUMENT, L"Read", error, errorLength);
    }

    DrsSession session;
    NvAPI_Status result = session.Open();
    if (result != NVAPI_OK)
    {
        return Fail(result, L"Open NVIDIA settings", error, errorLength);
    }

    for (std::uint32_t index = 0; index < count; ++index)
    {
        existed[index] = 0;
        values[index] = 0;
    }
    CopyText(profileName, profileNameLength, L"");

    NvDRSProfileHandle profile = nullptr;
    result = FindProfile(session.Handle(), executable, &profile);
    if (result == NVAPI_EXECUTABLE_NOT_FOUND)
    {
        CopyText(error, errorLength, L"");
        return NVAPI_OK;
    }
    if (result != NVAPI_OK)
    {
        return Fail(result, L"Find game profile", error, errorLength);
    }
    CopyText(profileName, profileNameLength, ProfileName(session.Handle(), profile));

    for (std::uint32_t index = 0; index < count; ++index)
    {
        NVDRS_SETTING setting{};
        setting.version = NVDRS_SETTING_VER;
        result = NvAPI_DRS_GetSetting(session.Handle(), profile, ids[index], &setting);
        if (result == NVAPI_OK)
        {
            existed[index] = setting.settingLocation == NVDRS_CURRENT_PROFILE_LOCATION ? 1U : 0U;
            values[index] = setting.u32CurrentValue;
        }
        else if (result != NVAPI_SETTING_NOT_FOUND)
        {
            return Fail(result, L"Read setting", error, errorLength);
        }
    }

    CopyText(error, errorLength, L"");
    return NVAPI_OK;
}

// Writes values[i] for ids[i], or deletes the setting from the profile when
// remove[i] is 1. All changes are saved once at the end.
SMITEPATCH_API int __cdecl SmitePatchNv_Write(
    const wchar_t* executable,
    const std::uint32_t* ids,
    const std::uint32_t* values,
    const std::uint32_t* remove,
    const std::uint32_t count,
    wchar_t* error,
    const std::uint32_t errorLength)
{
    if (executable == nullptr || ids == nullptr || values == nullptr || remove == nullptr)
    {
        return Fail(NVAPI_INVALID_ARGUMENT, L"Write", error, errorLength);
    }

    DrsSession session;
    NvAPI_Status result = session.Open();
    if (result != NVAPI_OK)
    {
        return Fail(result, L"Open NVIDIA settings", error, errorLength);
    }

    NvDRSProfileHandle profile = nullptr;
    result = EnsureProfile(session.Handle(), executable, &profile);
    if (result != NVAPI_OK)
    {
        return Fail(result, L"Find game profile", error, errorLength);
    }

    for (std::uint32_t index = 0; index < count; ++index)
    {
        if (remove[index] != 0)
        {
            result = NvAPI_DRS_DeleteProfileSetting(session.Handle(), profile, ids[index]);
            if (result == NVAPI_SETTING_NOT_FOUND)
            {
                result = NVAPI_OK;
            }
        }
        else
        {
            NVDRS_SETTING setting{};
            setting.version = NVDRS_SETTING_VER;
            setting.settingId = ids[index];
            setting.settingType = NVDRS_DWORD_TYPE;
            setting.u32CurrentValue = values[index];
            result = NvAPI_DRS_SetSetting(session.Handle(), profile, &setting);
        }
        if (result != NVAPI_OK)
        {
            return Fail(result, L"Write setting", error, errorLength);
        }
    }

    result = NvAPI_DRS_SaveSettings(session.Handle());
    if (result != NVAPI_OK)
    {
        return Fail(result, L"Save NVIDIA settings", error, errorLength);
    }
    CopyText(error, errorLength, L"");
    return NVAPI_OK;
}

// Self-test cleanup: deletes the profile of a test executable, but only the
// fallback profile this helper creates, never a driver-shipped game profile.
SMITEPATCH_API int __cdecl SmitePatchNv_DeleteFallbackProfile(const wchar_t* executable, wchar_t* error, const std::uint32_t errorLength)
{
    DrsSession session;
    NvAPI_Status result = session.Open();
    if (result != NVAPI_OK)
    {
        return Fail(result, L"Open NVIDIA settings", error, errorLength);
    }
    NvDRSProfileHandle profile = nullptr;
    result = FindProfile(session.Handle(), executable, &profile);
    if (result == NVAPI_EXECUTABLE_NOT_FOUND)
    {
        CopyText(error, errorLength, L"");
        return NVAPI_OK;
    }
    if (result != NVAPI_OK)
    {
        return Fail(result, L"Find profile", error, errorLength);
    }
    if (ProfileName(session.Handle(), profile) != FallbackProfileName)
    {
        return Fail(NVAPI_INVALID_ARGUMENT, L"Refusing to delete a profile the patcher did not create", error, errorLength);
    }
    result = NvAPI_DRS_DeleteProfile(session.Handle(), profile);
    if (result == NVAPI_OK)
    {
        result = NvAPI_DRS_SaveSettings(session.Handle());
    }
    if (result != NVAPI_OK)
    {
        return Fail(result, L"Delete profile", error, errorLength);
    }
    CopyText(error, errorLength, L"");
    return NVAPI_OK;
}
