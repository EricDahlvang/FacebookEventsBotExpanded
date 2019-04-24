﻿using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Newtonsoft.Json;

namespace Microsoft.BotBuilderSamples.FacebookModel
{
    public static class FacebookThreadControlHelper
    {
        public const string GraphApiBaseUrl = "https://graph.facebook.com/v2.6/me/{0}?access_token={1}";

        private static async Task<bool> PostAsync(string postType, string pageToken, string content)
        {
            var requestPath = string.Format(GraphApiBaseUrl, postType, pageToken);
            var stringContent = new StringContent(content, Encoding.UTF8, "application/json");

            // Create HTTP transport objects
            using (var requestMessage = new HttpRequestMessage())
            {
                requestMessage.Method = new HttpMethod("POST");
                requestMessage.RequestUri = new Uri(requestPath);
                requestMessage.Content = stringContent;
                requestMessage.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/json; charset=utf-8");

                using (var client = new HttpClient())
                {
                    // Make the Http call
                    using (var response = await client.SendAsync(requestMessage, CancellationToken.None).ConfigureAwait(false))
                    {
                        // Return true if the call was successfull
                        System.Diagnostics.Debug.Print(await response.Content.ReadAsStringAsync());
                        return response.IsSuccessStatusCode;
                    }
                }
            }
        }

        public static async Task<bool> RequestThreadControlToBot(ITurnContext turnContext, string pageToken, string userId, string message)
        {
            long timestamp = DateTime.Now.Ticks;
            var hod = new { recipient = new { id = userId }, metadata = message };
            return await PostAsync("request_thread_control", pageToken, JsonConvert.SerializeObject(hod));
        }
        
        public static async Task<bool> PassThreadControlToPrimaryBot(ITurnContext turnContext, string pageToken, string userId, string message)
        {
            long timestamp = DateTime.Now.Ticks;
            var hod = new { recipient = new { id = userId }, metadata = message };
            return await PostAsync("take_thread_control", pageToken, JsonConvert.SerializeObject(hod));
        }

        public static async Task<bool> PassThreadControlToBot(ITurnContext turnContext, string pageToken, string targetAppId, string userId, string message)
        {
            var hod = new { recipient = new { id = userId }, target_app_id = targetAppId, metadata = message };
            return await PostAsync("pass_thread_control", pageToken, JsonConvert.SerializeObject(hod));
        }
    }

}
