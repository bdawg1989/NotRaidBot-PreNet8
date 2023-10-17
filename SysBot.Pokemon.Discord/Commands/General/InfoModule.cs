﻿using Discord;
using Discord.Commands;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SharedUtils;

namespace SysBot.Pokemon.Discord
{
    // src: https://github.com/foxbot/patek/blob/master/src/Patek/Modules/InfoModule.cs
    // ISC License (ISC)
    // Copyright 2017, Christopher F. <foxbot@protonmail.com>
    public class InfoModule : ModuleBase<SocketCommandContext>
    {
        private const string detail = "I am a custom Raid Bot made by Gengar and Kai that accepts raid requests, and much more.";
        public const string version = SharedConstants.Version;
        private const string support = "https://notpaldea.com";

        [Command("info")]
        [Alias("about", "whoami", "owner")]
        public async Task InfoAsync()
        {
            var app = await Context.Client.GetApplicationInfoAsync().ConfigureAwait(false);
            var programIconUrl = "https://genpkm.com/images/icon4.png";
            var builder = new EmbedBuilder
            {
                Color = new Color(114, 137, 218),
                Description = detail,
                ImageUrl = programIconUrl
            };

            builder.AddField("# __Bot Info__",
                $"- **Version**: {version}\n" +
                $"- [Download NotRaidBot]({support})\n" +
                $"- {Format.Bold("Owner")}: {app.Owner} ({app.Owner.Id})\n" +
                $"- {Format.Bold("Library")}: Discord.Net ({DiscordConfig.Version})\n" +
                $"- {Format.Bold("Uptime")}: {GetUptime()}\n" +
                $"- {Format.Bold("Runtime")}: {RuntimeInformation.FrameworkDescription} {RuntimeInformation.ProcessArchitecture} " +
                $"({RuntimeInformation.OSDescription} {RuntimeInformation.OSArchitecture})\n" +
                $"- {Format.Bold("Buildtime")}: {GetVersionInfo("SysBot.Base", false)}\n" +
                $"- {Format.Bold("Core Version")}: {GetVersionInfo("PKHeX.Core")}\n" +
                $"- {Format.Bold("AutoLegality Version")}: {GetVersionInfo("PKHeX.Core.AutoMod")}\n"
                );

            builder.AddField("Stats",
                $"- {Format.Bold("Guilds")}: {Context.Client.Guilds.Count}\n" +
                $"- {Format.Bold("Channels")}: {Context.Client.Guilds.Sum(g => g.Channels.Count)}\n" +
                $"- {Format.Bold("Users")}: {Context.Client.Guilds.Sum(g => g.MemberCount)}\n" +
                $"{Format.Bold("\nVisit [NotPaldea.com](https://notpaldea.com) for more information.")}\n"
                );

            await ReplyAsync("Here's a bit about me!", embed: builder.Build()).ConfigureAwait(false);
        }

        private static string GetUptime() => (DateTime.Now - Process.GetCurrentProcess().StartTime).ToString(@"dd\.hh\:mm\:ss");
        private static string GetVersionInfo(string assemblyName, bool inclVersion = true)
        {
            const string _default = "Unknown";
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var assembly = assemblies.FirstOrDefault(x => x.GetName().Name == assemblyName);
            if (assembly is null)
                return _default;

            var attribute = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (attribute is null)
                return _default;

            var info = attribute.InformationalVersion;
            var split = info.Split('+');
            if (split.Length >= 2)
            {
                var version = split[0];
                var revision = split[1];
                if (DateTime.TryParseExact(revision, "yyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var buildTime))
                    return inclVersion ? $"{version} ({buildTime:yy-MM-dd\\.hh\\:mm})" : buildTime.ToString(@"yy-MM-dd\.hh\:mm");
                return inclVersion ? version : _default;
            }
            return _default;
        }
    }
}