// FlexPad (cross-platform) - programmable command buttons and FlexControl knob for a FlexRadio
// Copyright (C) 2026  David Erickson (AB0R)
//
// This program is free software: you can redistribute it and/or modify it under the
// terms of the GNU General Public License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful, but WITHOUT ANY
// WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A
// PARTICULAR PURPOSE.  See the GNU General Public License for more details. You should
// have received a copy of the GNU General Public License along with this program (see
// the LICENSE file).  If not, see <https://www.gnu.org/licenses/>.

using Avalonia;
using FlexPad.App.Services;
using FlexPad.Core;

namespace FlexPad.App;

internal static class Program
{
    /// <summary>
    /// An interactive <c>--uninstall</c> was asked for, so the UI starts solely to ask what to do
    /// about the settings. Read by <see cref="App"/> once a window is up.
    /// </summary>
    public static bool PendingUninstall { get; private set; }

    // Avalonia entry point. Don't use any Avalonia/UI types before AppMain is called.
    [STAThread]
    public static void Main(string[] args)
    {
        // First thing, before the install switches and before Avalonia: a crash during startup on an
        // unfamiliar machine is the one a tester is most likely to hit and least able to describe.
        CrashLog.Install();

        var request = InstallCommandLine.Parse(args);

        // These run without a UI, so an exception here would surface as a crash dump rather than as
        // anything a user could act on. Report through the exit code and go quietly instead.
        try
        {
            switch (request.Action)
            {
                // Installing needs no UI: copy, register, and hand off to the installed copy.
                case InstallAction.Install:
                    var installed = InstallService.Install();
                    if (!request.Quiet) InstallService.LaunchDetached(installed.ExePath);
                    // The program is installed either way, so this is not a failure — but a script
                    // that cares whether the app is removable can tell the difference.
                    if (!installed.Registered) Environment.ExitCode = 2;
                    return;

                // An unattended uninstall has nobody to ask, so it takes only the program and leaves
                // the meter list and cable pinning behind.
                case InstallAction.Uninstall when request.Quiet:
                    InstallService.Uninstall(new UninstallOptions(RemoveSettings: false));
                    return;

                case InstallAction.Uninstall:
                    PendingUninstall = true;
                    break;
            }
        }
        catch (Exception ex)
        {
            // These run without a UI, so the exit code is all a script sees and the user sees nothing
            // at all. Record it: a failed --install is otherwise indistinguishable from one that
            // worked, which is precisely the silence the crash log exists to break.
            CrashLog.Write("install", ex);
            Environment.ExitCode = 1;
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
