using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Discord.Rest;
using Discord.Commands;
using System.IO;
using Microsoft.Extensions.DependencyInjection;

namespace DM_Forwarder
{
    class Program
    {
        static void Main(string[] args) =>
            new Program().RunAsync().GetAwaiter().GetResult();

        private DiscordSocketClient socketClient;
        private DiscordRestClient restClient;
        private Config config;

        private async Task RunAsync()
        {
            socketClient = new DiscordSocketClient(new DiscordSocketConfig
            {
                LogLevel = LogSeverity.Verbose
            });
            socketClient.Log += Log; // Set up a method for logs to be output to the console window

            restClient = new DiscordRestClient(new DiscordRestConfig
            {
                LogLevel = LogSeverity.Verbose
            });
            restClient.Log += Log;

            config = Config.Load(); // Load the config file

            await socketClient.LoginAsync(TokenType.Bot, config.Token);
            await socketClient.StartAsync(); // Log in and keep it running in the background

            await restClient.LoginAsync(TokenType.Bot, config.Token);

            socketClient.MessageReceived += Client_MessageReceived;

            await Task.Delay(-1); // Prevent the bot from exiting immediately
        }

        private async Task Client_MessageReceived(SocketMessage msg)
        {
            // TODO: Implement error catching, and ensure that the report was actually forwarded before replying.
            // TODO: Check if there is more than one staff role in use, or if standard members should also be allowed to report

            // Optional: Implement a command to reply from the manager channel directly to the DM to simulate
            //           a group DM. Or maybe it'd be easier to create a real group DM and keep the bot simple.

            if (msg.Author.Id == socketClient.CurrentUser.Id || msg.Author.IsBot)
                return; // Don't respond to our own messages or other bots to help prevent loops

            if ((msg.Channel as SocketGuildChannel).Guild == null) // Check that this is a DM (no guild associated with it)
            {
                SocketGuildUser socketUser = socketClient.GetGuild(config.MainGuild).GetUser(msg.Author.Id);
                // Attempt to see if the user object is cached and in the target guild
                RestGuildUser restUser = null;

                List<ulong> roles = new List<ulong>();

                if (socketUser != null) // If the user is in our guild, store their roles
                    roles.AddRange(socketUser.Roles.Select(x => x.Id));
                else // Otherwise ensure it isn't a caching issue by attempting to manually download the user
                    restUser = await restClient.GetGuildUserAsync(config.MainGuild, msg.Author.Id);

                if (roles.Count() == 0 && restUser == null)
                    return; // The user isn't in the server
                else if (restUser != null) // Store the roles from the downloaded user
                    roles.AddRange(restUser.RoleIds);

                if (!roles.Contains(config.StaffRole))
                    return; // Return if the user isn't a staff member

                // At this point we've determined that the user is in the server and has the staff role

                string source = $"Message receieved from {msg.Author.Username}#{msg.Author.Discriminator} [{msg.Author.Id}]"; // Information about who sent it to prevent abuse

                if (msg.Content.Length + source.Length + 2 < 2000) // Make sure we're not about to send a message larger than Discord allows
                    await (socketClient.GetChannel(config.ManagerChannel) as SocketTextChannel).SendMessageAsync($"{source}\n{msg.Content}");
                else
                {
                    await (socketClient.GetChannel(config.ManagerChannel) as SocketTextChannel).SendMessageAsync(source);
                    await (socketClient.GetChannel(config.ManagerChannel) as SocketTextChannel).SendMessageAsync(msg.Content);
                }

                await msg.Channel.SendMessageAsync($"Thank you for your report {msg.Author.Mention}, it has been forwarded to the relevant channel.");
            }
        }

        // Attempt to format log messages
        private Task Log(LogMessage msg)
        {
            //Console.WriteLine(msg.ToString());

            //Color
            ConsoleColor color;
            switch (msg.Severity)
            {
                case LogSeverity.Error: color = ConsoleColor.Red; break;
                case LogSeverity.Warning: color = ConsoleColor.Yellow; break;
                case LogSeverity.Info: color = ConsoleColor.White; break;
                case LogSeverity.Verbose: color = ConsoleColor.Gray; break;
                case LogSeverity.Debug: default: color = ConsoleColor.DarkGray; break;
            }

            //Exception
            string exMessage;
            Exception ex = msg.Exception;
            if (ex != null)
            {
                while (ex is AggregateException && ex.InnerException != null)
                    ex = ex.InnerException;
                exMessage = $"{ex.Message}";
                if (exMessage != "Reconnect failed: HTTP/1.1 503 Service Unavailable")
                    exMessage += $"\n{ex.StackTrace}";
            }
            else
                exMessage = null;

            //Source
            string sourceName = msg.Source?.ToString();

            //Text
            string text;
            if (msg.Message == null)
            {
                text = exMessage ?? "";
                exMessage = null;
            }
            else
                text = msg.Message;
            
            if (sourceName == "Command")
                color = ConsoleColor.Cyan;
            else if (sourceName == "<<Message")
                color = ConsoleColor.Green;
            else if (sourceName == ">>Message")
                return Task.CompletedTask;

            //Build message
            StringBuilder builder = new StringBuilder(text.Length + (sourceName?.Length ?? 0) + (exMessage?.Length ?? 0) + 5);
            if (sourceName != null)
            {
                builder.Append('[');
                builder.Append(sourceName);
                builder.Append("] ");
            }
            builder.Append($"[{DateTime.Now.ToString("d")} {DateTime.Now.ToString("T")}] ");
            for (int i = 0; i < text.Length; i++)
            {
                //Strip control chars
                char c = text[i];
                if (c == '\n' || !char.IsControl(c) || c != (char)8226)
                    builder.Append(c);
            }
            if (exMessage != null)
            {
                builder.Append(": ");
                builder.Append(exMessage);
            }

            text = builder.ToString();
            //if (msg.Severity <= LogSeverity.Info)
            //{
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            //}
            #if DEBUG
            System.Diagnostics.Debug.WriteLine(text);
            #endif

            return Task.CompletedTask;
        }
    }
}
