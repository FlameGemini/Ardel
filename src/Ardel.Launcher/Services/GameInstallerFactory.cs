using System.Net.Http;
using CmlLib.Core.Installers;
using Ardel.Launcher.Models;

namespace Ardel.Launcher.Services;

internal static class GameInstallerFactory
{
    public static IGameInstaller Create(HttpClient http, ArdoInstallOptions? options = null) =>
        new ArdelGameInstaller(http, options);
}
