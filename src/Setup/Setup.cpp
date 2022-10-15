#include <windows.h>
#include <versionhelpers.h>
#include <string>
#include <fstream>
#include <winnls.h>
#include "bundle_marker.h"
#include "platform_util.h"

#define OS_NOTSUPPORTED_EN L"This application requires Windows 8 or later and cannot be installed on this computer."
#define OS_NOTSUPPORTED_UK L"Ця програма потребує Windows 8 або новішої версії, тому її неможливо встановити на цьому комп’ютері."
#define OS_NOTSUPPORTED_ES L"Esta aplicación requiere Windows 8 o posterior y no se puede instalar en esta computadora."
#define OS_NOTSUPPORTED_PT L"Este aplicativo requer o Windows 8 ou posterior e não pode ser instalado neste computador."
#define OS_NOTSUPPORTED_NL L"Deze applicatie vereist Windows 8 of hoger en kan niet op deze computer worden geïnstalleerd."
#define OS_NOTSUPPORTED_CN L"此应用程序需要 Windows 8 或更高版本，无法安装在此计算机上。"

using namespace std;

int WINAPI wWinMain(_In_ HINSTANCE hInstance, _In_opt_ HINSTANCE hPrevInstance, _In_ PWSTR pCmdLine, _In_ int nCmdShow)
{
    if (!IsWindows8OrGreater()) {
        LCID localeId = GetThreadLocale();
        WCHAR langNameBuff[5];
        if (GetLocaleInfo(localeId, LOCALE_SISO639LANGNAME, langNameBuff, 5) == 0) {
            util::show_error_dialog(OS_NOTSUPPORTED_EN);
        }
        else {
            wstring langName(langNameBuff);
	        if (langName._Equal(L"uk")) util::show_error_dialog(OS_NOTSUPPORTED_UK);
            else if (langName._Equal(L"es")) util::show_error_dialog(OS_NOTSUPPORTED_ES);
            else if (langName._Equal(L"pt")) util::show_error_dialog(OS_NOTSUPPORTED_PT);
            else if (langName._Equal(L"nl")) util::show_error_dialog(OS_NOTSUPPORTED_NL);
            else if (langName._Equal(L"cn")) util::show_error_dialog(OS_NOTSUPPORTED_CN);
            else util::show_error_dialog(OS_NOTSUPPORTED_EN);
        }
        return 0;
    }

    wstring updaterPath = util::get_temp_file_path(L"exe");
    wstring packagePath = util::get_temp_file_path(L"nupkg");
    uint8_t* memAddr = 0;

    try {
        // locate bundled package and map to memory
        memAddr = util::mmap_read(util::get_current_process_path(), 0);
        if (!memAddr) {
            throw wstring(L"Unable to memmap current executable. Is there enough available system memory?");
        }

        int64_t packageOffset, packageLength;
        bundle_marker_t::header_offset(&packageOffset, &packageLength);
        uint8_t* pkgStart = memAddr + packageOffset;
        if (packageOffset == 0 || packageLength == 0) {
            throw wstring(L"The embedded package containing the application to install was not found. Please contact the application author.");
        }

        // rough check for sufficient disk space before extracting anything
        // required space is size of compressed nupkg, size of extracted app, 
        // and squirrel overheads (incl temp files). the constant 0.38 is a
        // aggressive estimate on what the compression ratio might be.
        int64_t squirrelOverhead = 50 * 1000 * 1000;
        int64_t requiredSpace = squirrelOverhead + (packageLength * 3) + (packageLength / (double)0.38);
        if (!util::check_diskspace(requiredSpace)) {
            throw wstring(L"Insufficient disk space. This application requires at least " + util::pretty_bytes(requiredSpace) + L" free space to be installed.");
        }

        // extract Update.exe and embedded nuget package
        util::extractUpdateExe(pkgStart, packageLength, updaterPath);
        std::ofstream(packagePath, std::ios::binary).write((char*)pkgStart, packageLength);

        // run installer and forward our command line arguments
        wstring cmd = L"\"" + updaterPath + L"\" --setup \"" + packagePath + L"\" " + pCmdLine;
        util::wexec(cmd.c_str());
    }
    catch (wstring wsx) {
        util::show_error_dialog(L"An error occurred while running setup. " + wsx);
    }
    catch (...) {
        util::show_error_dialog(L"An unknown error occurred while running setup. Please contact the application author.");
    }

    // clean-up resources
    if (memAddr) util::munmap(memAddr);
    DeleteFile(updaterPath.c_str());
    DeleteFile(packagePath.c_str());
    return 0;
}