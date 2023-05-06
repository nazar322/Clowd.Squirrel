[![Nuget](https://img.shields.io/nuget/v/Clowd.Squirrel?style=flat-square)](https://www.nuget.org/packages/Clowd.Squirrel/)
[![Nuget (with prereleases)](https://img.shields.io/nuget/vpre/Clowd.Squirrel?style=flat-square)](https://www.nuget.org/packages/Clowd.Squirrel/)
[![Discord](https://img.shields.io/discord/767856501477343282?style=flat-square&color=purple)](https://discord.gg/CjrCrNzd3F)
[![Build](https://img.shields.io/github/actions/workflow/status/clowd/Clowd.Squirrel/build.yml?branch=develop&style=flat-square)](https://github.com/clowd/Clowd.Squirrel/actions)

# Clowd.Squirrel

Squirrel is both a set of tools and a library, to completely manage both installation and updating your desktop application. 

Feel free to join our discord to recieve updates or to ask questions:

[![discordimg2](https://user-images.githubusercontent.com/1287295/150318745-cbfcf5d0-3697-4bef-ac1a-b0d751f53b48.png)](https://discord.gg/CjrCrNzd3F)

---

## Looking for info on v3.0 / cross-platform?
Clowd.Squirrel v3.0 is a ground-up re-write and now supports macos (linux in the future?). Documentation for this is very limited but I have started working on this [in the docs-v3 folder](docs-v3). This work is being done in the `develop` branch.

I will continue to support 2.x with critical fixes until further notice. This is currently the `master` branch.

---

## What Do We Want?

Apps should be as fast easy to install. Update should be seamless like Google Chrome. From a developer's side, it should be really straightforward to create an installer for my app, and publish updates to it, without having to jump through insane hoops. 

* **Integrating** an app to use Squirrel should be extremely easy, provide a client API, and be developer friendly.
* **Packaging** is really easy, can be automated, and supports delta update packages.
* **Distributing** should be straightforward, use simple HTTP updates, and provide multiple "channels" (a-la Chrome Dev/Beta/Release).
* **Installing** is Wizard-Free™, with no UAC dialogs, does not require reboot, and is .NET Framework friendly.
* **Updating** is in the background, doesn't interrupt the user, and does not require a reboot.

---

## Migrating from Squirrel.Windows?

 - The command line interface for Squirrel.exe is different. Check 'Squirrel.exe -h' for more info.
 - The command line for Update.exe here is compatible with the old Squirrel.
 - Update.exe here is bigger and is included in your packages. This means Update.exe will be updated each time you update your app. As long as you build delta packages, this will not impact the size of your updates.
 - Migrating to this library is fully compatible, except for the way we detect SquirrelAware binaries. Follow the Quick Start guide.
 - There have been a great many other improvements here. To see some of them [have a look at the feature matrix](#feature-matrix).
 - Something detected as a virus? This was an issue at the old Squirrel, and also see [issue #28](https://github.com/clowd/Clowd.Squirrel/issues/28)

---

## Quick Start For .NET Apps

1. Install the [Clowd.Squirrel Nuget Package](https://www.nuget.org/packages/Clowd.Squirrel/)

2. Add SquirrelAwareVersion to your assembly manifest to indicate that your exe supports Squirrel. 
Note: In newer .NET Core versions you first need to add the Application Manifest through New Item window.

   ```xml
   <?xml version="1.0" encoding="utf-8"?>
   <assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
     <SquirrelAwareVersion xmlns="urn:schema-squirrel-com:asm.v1">1</SquirrelAwareVersion>
   </assembly>
   ```

3. Handle Squirrel events somewhere very early in your application startup (such as the beginning of `main()` or `Application.OnStartup()` for WPF). 

   ```cs
   public static void Main(string[] args)
   {
       // run Squirrel first, as the app may exit after these run
       SquirrelAwareApp.HandleEvents(
           onInitialInstall: OnAppInstall,
           onAppUninstall: OnAppUninstall,
           onEveryRun: OnAppRun);

       // ... other app init code after ...
   }

   private static void OnAppInstall(SemanticVersion version, IAppTools tools)
   {
       tools.CreateShortcutForThisExe(ShortcutLocation.StartMenu | ShortcutLocation.Desktop);
   }

   private static void OnAppUninstall(SemanticVersion version, IAppTools tools)
   {
       tools.RemoveShortcutForThisExe(ShortcutLocation.StartMenu | ShortcutLocation.Desktop);
   }

   private static void OnAppRun(SemanticVersion version, IAppTools tools, bool firstRun)
   {
       tools.SetProcessAppUserModelId();
       // show a welcome message when the app is first installed
       if (firstRun) MessageBox.Show("Thanks for installing my application!");
   }
   ```
   
   When installed, uninstalled or updated, these methods will be executed, giving your app a chance to add or remove application shortcuts or perform other tasks. 

4. Build/Publish your app (with `msbuild`, `dotnet publish` or similar)

5. Create a Squirrel release using the `Squirrel.exe` command line tool. 
   The tool can be downloaded from GitHub Releases, and it is also bundled into the [Clowd.Squirrel](https://www.nuget.org/packages/Clowd.Squirrel/) nuget package. 
   If installed through NuGet, the tools can usually be found at:
   - `%userprofile%\.nuget\packages\clowd.squirrel\2.9.40\tools`, or;
   - `..\packages\clowd.squirrel\2.9.40\tools`
   
   Once you have located the tools folder, create a release. Minimal example below with some useful options, but explore `Squirrel.exe -h` for a complete list.
   ```cmd
   Squirrel.exe pack --packId "YourApp" --packVersion "1.0.0" --packDirectory "path-to/publish/folder"
   ```
   Important Notes:
   - The same `--releaseDir` (default `.\Releases` if not specified) should be used each time, so delta updates can be generated.
   - The package version must comply to strict 3-part SemVer syntax. (eg. `1.0.0`, `1.0.1-pre`)
   - A list of supported runtimes for the `--framework` argument is [available here](https://github.com/clowd/Clowd.Squirrel/blob/develop/src/Squirrel/Runtimes.cs)
   
6. Distribute your entire `--releaseDir` folder online. This folder can be hosted on any static web/file server, [Amazon S3](docs/using/amazon-s3.md), BackBlaze B2, or even via [GitHub Releases](docs/using/github.md). 
   
   If using CI to deploy releases, you can use the package syncing commands to download the currently live version, before creating a package. This means delta/patch updates can be generated. Complete powershell example:
   ```ps1
   # build / publish your app
   dotnet publish -c Release -o ".\publish" 

   # find Squirrel.exe path and add an alias
   Set-Alias Squirrel ($env:USERPROFILE + "\.nuget\packages\clowd.squirrel\2.9.40\tools\Squirrel.exe");

   # download currently live version
   Squirrel http-down --url "https://the.place/you-host/updates"

   # build new version and delta updates.
   Squirrel pack`
    --framework net6,vcredist143-x86`  # Install .NET 6.0 (x64) and vcredist143 (x86) during setup, if not installed
    --packId "YourApp"`                # Application / package name
    --packVersion "1.0.0"`             # Version to build. Should be supplied by your CI
    --packAuthors "YourCompany"`       # Your name, or your company name
    --packDir ".\publish"`       # The directory the application was published to
    --icon "mySetupIcon.ico"`     # Icon for Setup.exe and Update.exe
    --splashImage "install.gif"        # The splash artwork (or animation) to be shown during install
   ```

7. Update your app on startup / periodically with UpdateManager.
   ```cs
   private static async Task UpdateMyApp()
   {
      using var mgr = new UpdateManager("https://the.place/you-host/updates");
      var newVersion = await mgr.UpdateApp();
      
      // optionally restart the app automatically, or ask the user if/when they want to restart
      if (newVersion != null) {
         UpdateManager.RestartApp();
      }
   }
   ```

---

## Feature Matrix

| Feature | Clowd.Squirrel | Squirrel.Windows |
|---|---|---|
| Continuous updates, bug fixes, and other improvements | ✅ | ❌ |
| Provides a command line update interface (Update.exe) with your app | ✅ | ✅ |
| Update.exe Size | ❌ 12.5mb | ✅ 2mb |
| Provides a C# SDK | netstandard2.0<br>net461<br>net5.0<br>net6.0 | netstandard2.0 |
| SDK has 100% XML comment coverage in Nuget Pacakge | ✅ | None, does not ship comments in NuGet |
| SDK Dependencies | SharpCompress | SharpCompress (outdated & security vulnerability)<br>NuGet (outdated and bugs)<br>Mono.Cecil (outdated and bugs)<br>Microsoft.Web.Xdt<br>Microsoft.CSharp<br>Microsoft.Win32.Registry<br>System.Drawing.Common<br>System.Net.Http<br>System.Web |
| SDK is strong-name signed | ✅ | ❌ |
| Provides an update package builder (Squirrel.exe) | ✅ | ✅ |
| Supports building tiny delta updates | ✅ | ✅ |
| Can compile a release/setup in a single easy command | ✅ | ❌ |
| Command line tool for package building that actually prints helpful messages to the console | ✅ | ❌ |
| CLI help text that is command-based and easily understandable | ✅ | ❌ |
| Supports building packages for native apps | ✅ | ✅ |
| Supports building packages for .Net/Core | ✅ | Limited/Buggy |
| Supports building packages for PublishSingleFile apps | ✅ | ❌ |
| Supports fully automated CI package deployments easily | ✅ | ❌ |
| Compiles an installer (Setup.exe) | ✅ | ✅ |
| Setup Splash Gif | ✅ | ✅ |
| Setup Splash Png,Jpeg,Tiff,Etc | ✅ | ❌ |
| Setup Splash Progress Bar | ✅ | ❌ |
| Setup Splash has Multi-Monitor DPI support | ✅ | ❌ |
| No internal dependencies on external frameworks/runtimes | ✅ | ❌ |
| Can deploy an application that has no dependencies | ✅ | ❌ (always installs .Net Framework with your app) |
| Can install .Net Full Framework during setup | ✅ | ✅ |
| Can install .Net/Core during setup | ✅ | ❌ |
| Can install vcredist during setup | ✅ | ❌ |
| Can install new runtimes (see above) during updates | ✅ | ❌ |
| Cleans up after itself | ✅ | Leaves huge log files everywhere<br>Does not delete itself during uninstall |
| Can build an MSI enterprise machine-wide deployment tool | ✅ | ✅ |

---

## Building Squirrel
For the impatient:

```cmd
git clone https://github.com/clowd/Clowd.Squirrel
cd clowd/Clowd.Squirrel
build.cmd
```

See [Contributing](docs/contributing/contributing.md) for additional information on building and contributing to Squirrel.

## License and Usage

See [COPYING](COPYING) for details on copyright and usage.
