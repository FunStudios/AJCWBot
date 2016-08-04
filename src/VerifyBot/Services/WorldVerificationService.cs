﻿using Discord;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using VerifyBot.Gw2Api;
using VerifyBot.Models;

namespace VerifyBot.Service
{
    public class WorldVerificationService
    {
        private const string AccountNameApiKeyRegex = @"\s*(.+?\.\d+)\s+(.*?-.*?-.*?-.*?-.*)\s*$";
        private const int APIKeyLength = 72;

        private readonly IDiscordClient client;

        private readonly VerifyContext db;

        private Configuration config;

        public WorldVerificationService(IDiscordClient client, Configuration config)
        {
            this.client = client;
            this.config = config;
            this.db = new VerifyContext();
        }

        public async Task Process(IMessage e)
        {
            try
            {
                if (e.Channel is IGuildChannel)
                {
                    if ((e.Channel as IGuildChannel).Name != this.config.VerifyChannelName)
                    {
                        return;
                    }

                    if (e.Content.ToLower().Contains("!verify"))
                    {
                        var user = e.Author as IGuildUser;
                        var pm = await user.CreateDMChannelAsync();

                        await pm.SendMessageAsync($"Respond to this bot with the following information: {{account-name}} {{api-key}} (Without the {{ or }} characters)");
                    }

                    await e.DeleteAsync();
                    return;
                }

                if (e.Channel is IDMChannel)
                {
                    await this.PerformVerification(e);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.ToString()}");
            }
        }

        private async Task PerformVerification(IMessage e)
        {
            try
            {
                Console.WriteLine($"Begin verification for user {e.Author.Username}");
                await e.Channel.SendMessageAsync("Starting Verification Process...");

                var tokens = new Regex(AccountNameApiKeyRegex).Split(e.Content);

                if (tokens.Length != 4)
                {
                    await e.Channel.SendMessageAsync("Invalid arguments.");
                    Console.WriteLine($"Could not verify {e.Author.Username} - Bad # of arguments");
                    return;
                }

                if (tokens[2].Length != 72)
                {
                    await e.Channel.SendMessageAsync("Invalid API Key.");
                    Console.WriteLine($"Could not verify {e.Author.Username} - Bad API Key");
                    return;
                }

                // Check GW2 server
                var api = new ApiFacade(tokens[2]);
                var account = await api.GetAccountAsync();

                if (account == null)
                {
                    await e.Channel.SendMessageAsync("Could not find that account in the GW2 API.");
                    Console.WriteLine($"Could not verify {e.Author.Username} - Cannont access account in GW2 API.");
                    return;
                }

                if (account.Name.ToLower() != tokens[1].ToLower())
                {
                    await e.Channel.SendMessageAsync("API Key account does not match supplied account name. (Case matters)");
                    Console.WriteLine($"Could not verify {e.Author.Username} - API Key account does not match supplied account. (Case matters)");
                    return;
                }

                if (!this.config.WorldIDs.Contains(account.WorldId))
                {
                    await e.Channel.SendMessageAsync("Account is not on JQ.");
                    Console.WriteLine($"Could not verify {e.Author.Username} - Not on JQ.");
                    return;
                }

                var existingUser = this.db.Users.FirstOrDefault(x => x.AccountID == account.Id);

                if (existingUser != null)
                {
                    await e.Channel.SendMessageAsync("Account is already verified. If you are having issues message a verifer");
                    return;
                }

                this.db.Users.Add(new User()
                {
                    AccountID = account.Id,
                    APIKey = tokens[2],
                    DiscordID = e.Author.Id
                });

                await this.db.SaveChangesAsync();

                var channel = e.Channel as IGuildChannel;
                var role = channel.Guild.Roles.Where(x => x.Name == this.config.VerifyRole)?.FirstOrDefault();
                var user = e.Author as IGuildUser;

                var currentRoles = user.Roles.ToList();

                if (!currentRoles.Contains(role))
                {
                    currentRoles.Add(role);

                    await user.ModifyAsync(x =>
                    {
                        x.Roles = currentRoles;
                    });
                }

                await e.Channel.SendMessageAsync("Verification Process Complete. Welcome to JQ discord");
                Console.WriteLine($"{e.Author.Username} Verified.");
            }
            catch (Exception ex)
            {
                await e.Channel.SendMessageAsync("Error processing your verification request. An entry has been logged.");
                Console.WriteLine($"Error: {ex.ToString()}");
            }
        }
    }
}