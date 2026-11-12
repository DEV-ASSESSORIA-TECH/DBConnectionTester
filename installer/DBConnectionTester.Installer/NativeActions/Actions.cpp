#include <windows.h>
#include <msiquery.h>
#include <restartmanager.h>
#include <shlobj.h>
#include <string>
#include <vector>
#include <cstdlib>

namespace
{
    constexpr auto SettingsKey = L"Software\\DBConnectionTester.Installer";

    std::wstring Property(MSIHANDLE session, const wchar_t* name)
    {
        DWORD count = 0;
        wchar_t empty = L'\0';
        const auto result = MsiGetPropertyW(session, name, &empty, &count);
        if (result != ERROR_MORE_DATA && result != ERROR_SUCCESS) throw result;
        std::wstring value(static_cast<size_t>(count) + 1, L'\0');
        ++count;
        if (MsiGetPropertyW(session, name, value.data(), &count) != ERROR_SUCCESS) throw ERROR_INSTALL_FAILURE;
        value.resize(count);
        return value;
    }

    void Set(MSIHANDLE session, const wchar_t* name, const std::wstring& value)
    {
        if (MsiSetPropertyW(session, name, value.c_str()) != ERROR_SUCCESS) throw ERROR_INSTALL_FAILURE;
    }

    bool Machine(MSIHANDLE session)
    {
        const auto allUsers = Property(session, L"ALLUSERS");
        return allUsers == L"1" || (allUsers == L"2" && Property(session, L"MSIINSTALLPERUSER").empty());
    }

    void Message(MSIHANDLE session, const wchar_t* text)
    {
        const auto record = MsiCreateRecord(0);
        MsiRecordSetStringW(record, 0, text);
        MsiProcessMessage(session, INSTALLMESSAGE_ERROR, record);
        MsiCloseHandle(record);
    }

    std::wstring SavedPathOption(bool machine)
    {
        wchar_t value[16]{};
        DWORD size = sizeof(value);
        const auto result = RegGetValueW(machine ? HKEY_LOCAL_MACHINE : HKEY_CURRENT_USER,
            SettingsKey, L"AddToPath", RRF_RT_REG_SZ | RRF_SUBKEY_WOW6464KEY, nullptr, value, &size);
        return result == ERROR_SUCCESS ? value : L"";
    }

    std::wstring NormalizePath(const std::wstring& path)
    {
        auto value = path;
        const auto first = value.find_first_not_of(L" \t\"");
        if (first == std::wstring::npos) return L"";
        value = value.substr(first, value.find_last_not_of(L" \t\"") - first + 1);
        const auto size = ExpandEnvironmentStringsW(value.c_str(), nullptr, 0);
        if (!size) throw ERROR_INSTALL_FAILURE;
        std::wstring expanded(size, L'\0');
        if (!ExpandEnvironmentStringsW(value.c_str(), expanded.data(), size)) throw ERROR_INSTALL_FAILURE;
        expanded.resize(size - 1);
        while (!expanded.empty() && (expanded.back() == L'\\' || expanded.back() == L'/')) expanded.pop_back();
        for (auto& character : expanded) if (character == L'/') character = L'\\';
        return expanded;
    }

    bool PathAlreadyExists(bool machine, const std::wstring& folder)
    {
        const auto key = machine ? L"SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Environment" : L"Environment";
        const auto root = machine ? HKEY_LOCAL_MACHINE : HKEY_CURRENT_USER;
        constexpr auto flags = RRF_RT_REG_SZ | RRF_RT_REG_EXPAND_SZ | RRF_NOEXPAND;
        DWORD size = 0;
        auto result = RegGetValueW(root, key, L"Path", flags, nullptr, nullptr, &size);
        if (result == ERROR_FILE_NOT_FOUND) return false;
        if (result != ERROR_SUCCESS) throw result;
        std::wstring value(static_cast<size_t>(size) / sizeof(wchar_t), L'\0');
        result = RegGetValueW(root, key, L"Path", flags, nullptr, value.data(), &size);
        if (result != ERROR_SUCCESS) throw result;
        value.resize(wcslen(value.c_str()));
        const auto expected = NormalizePath(folder);
        size_t start = 0;
        while (start < value.size())
        {
            const auto end = value.find(L';', start);
            const auto item = NormalizePath(value.substr(start, end == std::wstring::npos ? end : end - start));
            if (CompareStringOrdinal(item.c_str(), -1, expected.c_str(), -1, TRUE) == CSTR_EQUAL) return true;
            if (end == std::wstring::npos) break;
            start = end + 1;
        }
        return false;
    }

    bool OwnsPath(bool machine)
    {
        DWORD value = 0, size = sizeof(value);
        return RegGetValueW(machine ? HKEY_LOCAL_MACHINE : HKEY_CURRENT_USER, SettingsKey,
            machine ? L"MachinePath" : L"UserPath", RRF_RT_REG_DWORD | RRF_SUBKEY_WOW6464KEY,
            nullptr, &value, &size) == ERROR_SUCCESS && value == 1;
    }

    bool InSelectedContext(const std::wstring& product, bool machine)
    {
        DWORD size = 0;
        const auto context = machine ? MSIINSTALLCONTEXT_MACHINE : MSIINSTALLCONTEXT_USERUNMANAGED;
        const auto result = MsiGetProductInfoExW(product.c_str(), nullptr, context, INSTALLPROPERTY_VERSIONSTRING, nullptr, &size);
        if (result == ERROR_UNKNOWN_PRODUCT) return false;
        if (result != ERROR_MORE_DATA && result != ERROR_SUCCESS) throw result;
        return true;
    }

    std::vector<std::wstring> Split(const std::wstring& value)
    {
        std::vector<std::wstring> parts;
        size_t start = 0;
        while (start < value.size())
        {
            const auto end = value.find(L';', start);
            parts.push_back(value.substr(start, end == std::wstring::npos ? end : end - start));
            if (end == std::wstring::npos) break;
            start = end + 1;
        }
        return parts;
    }

    class RestartSession
    {
    public:
        DWORD Handle{};
        RestartSession()
        {
            wchar_t key[CCH_RM_SESSION_KEY + 1]{};
            if (RmStartSession(&Handle, 0, key) != ERROR_SUCCESS) throw ERROR_INSTALL_FAILURE;
        }
        ~RestartSession() { RmEndSession(Handle); }
        RestartSession(const RestartSession&) = delete;
        RestartSession& operator=(const RestartSession&) = delete;
    };

    bool FilesBusy(const std::wstring& folder)
    {
        RestartSession restart;
        const auto desktop = folder + L"DBConnectionTester.exe";
        const auto cli = folder + L"dbct.exe";
        const wchar_t* files[] = { desktop.c_str(), cli.c_str() };
        if (RmRegisterResources(restart.Handle, 2, files, 0, nullptr, 0, nullptr) != ERROR_SUCCESS)
            throw ERROR_INSTALL_FAILURE;
        UINT needed = 0, count = 0;
        DWORD reason = 0;
        auto result = RmGetList(restart.Handle, &needed, &count, nullptr, &reason);
        if (result == ERROR_MORE_DATA && needed > 0) return true;
        if (result != ERROR_SUCCESS) throw ERROR_INSTALL_FAILURE;
        return count > 0;
    }
}

extern "C" UINT __stdcall PrepareSettings(MSIHANDLE session)
{
    try
    {
        const bool machine = Machine(session);
        PWSTR root = nullptr;
        const auto result = SHGetKnownFolderPath(machine ? FOLDERID_ProgramFilesX64 : FOLDERID_UserProgramFiles,
            KF_FLAG_DONT_VERIFY, nullptr, &root);
        if (FAILED(result)) throw ERROR_INSTALL_FAILURE;
        const std::wstring folder = std::wstring(root) + L"\\DBConnectionTester\\";
        CoTaskMemFree(root);
        Set(session, L"INSTALLFOLDER", folder);
        PWSTR programs = nullptr;
        if (FAILED(SHGetKnownFolderPath(FOLDERID_CommonPrograms, KF_FLAG_DONT_VERIFY, nullptr, &programs))) throw ERROR_INSTALL_FAILURE;
        const std::wstring menu = std::wstring(programs) + L"\\DB Connection Tester\\";
        CoTaskMemFree(programs);
        Set(session, L"MachineProductMenu", menu);
        Set(session, L"DBCT_SCOPE_TEXT", machine ? L"Todos os usuários" : L"Somente este usuário");
        auto option = Property(session, L"ADD_TO_PATH");
        if (option.empty())
        {
            option = SavedPathOption(machine);
            if (option.empty()) option = L"1";
        }
        if (option != L"0" && option != L"1")
        {
            Message(session, L"ADD_TO_PATH deve ser 0 ou 1.");
            return ERROR_INSTALL_FAILURE;
        }
        Set(session, L"ADD_TO_PATH", option);
        Set(session, L"DBCT_PATH_DIRECTORY", folder.substr(0, folder.size() - 1));
        Set(session, L"DBCT_MANAGE_PATH", option == L"1" &&
            (OwnsPath(machine) || !PathAlreadyExists(machine, folder)) ? L"1" : L"0");
        Set(session, L"DBCT_UI_PATH", option == L"1" ? L"1" : L"");
        return ERROR_SUCCESS;
    }
    catch (...) { Message(session, L"Não foi possível preparar as opções de instalação."); return ERROR_INSTALL_FAILURE; }
}

extern "C" UINT __stdcall FilterRelatedProducts(MSIHANDLE session)
{
    try
    {
        for (auto name : { L"WIX_UPGRADE_DETECTED", L"WIX_DOWNGRADE_DETECTED" })
        {
            std::wstring selected;
            for (const auto& product : Split(Property(session, name)))
                if (InSelectedContext(product, Machine(session)))
                {
                    if (!selected.empty()) selected += L';';
                    selected += product;
                }
            Set(session, name, selected);
        }
        return ERROR_SUCCESS;
    }
    catch (...) { Message(session, L"Não foi possível identificar o contexto da instalação anterior."); return ERROR_INSTALL_FAILURE; }
}

extern "C" UINT __stdcall CheckFilesInUse(MSIHANDLE session)
{
    try
    {
        const auto folder = Property(session, L"INSTALLFOLDER");
        if (folder.empty()) return ERROR_INSTALL_FAILURE;
        while (FilesBusy(folder))
        {
            if (_wtoi(Property(session, L"UILevel").c_str()) < 4)
            {
                Message(session, L"Encerre a GUI e a CLI instaladas antes de instalar, reparar ou remover o produto. Nenhum processo foi encerrado.");
                return ERROR_INSTALL_FAILURE;
            }
            const auto record = MsiCreateRecord(0);
            MsiRecordSetStringW(record, 0, L"Encerre a GUI e a CLI desta instalação e selecione Repetir. Nenhum processo será encerrado automaticamente.");
            const auto response = MsiProcessMessage(session,
                static_cast<INSTALLMESSAGE>(INSTALLMESSAGE_USER | MB_RETRYCANCEL | MB_ICONWARNING), record);
            MsiCloseHandle(record);
            if (response != IDRETRY) return ERROR_INSTALL_USEREXIT;
        }
        return ERROR_SUCCESS;
    }
    catch (...) { Message(session, L"Não foi possível verificar os arquivos em uso. A manutenção foi interrompida."); return ERROR_INSTALL_FAILURE; }
}
