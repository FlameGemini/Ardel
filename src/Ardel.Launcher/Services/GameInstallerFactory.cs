using System.Net.Http;
using CmlLib.Core.Installers;

namespace Ardel.Launcher.Services;

internal static class GameInstallerFactory
{
    public static IGameInstaller Create(HttpClient http) => new ArdelGameInstaller(http);
}
