using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Bot.Connector.Authentication;
using Microsoft.Extensions.Configuration;

namespace Microsoft.BotBuilderSamples
{
    // <summary>
    /// A sample ICredentialProvider that is configured by multiple MicrosoftAppIds and MicrosoftAppPasswords
    /// </summary>
    public class MultiCredentialProvider : ICredentialProvider
    {
        public Dictionary<string, string> Credentials = new Dictionary<string, string>();

        public MultiCredentialProvider(IConfiguration configuration)
        {
            Credentials.Add(configuration["PrimaryMicrosoftAppId"], configuration["PrimaryMicrosoftAppPassword"]);
            Credentials.Add(configuration["SecondaryMicrosoftAppId"], configuration["SecondaryMicrosoftAppPassword"]);
        }

        public Task<bool> IsValidAppIdAsync(string appId)
        {
            return Task.FromResult(this.Credentials.ContainsKey(appId));
        }

        public Task<string> GetAppPasswordAsync(string appId)
        {
            return Task.FromResult(this.Credentials.ContainsKey(appId) ? this.Credentials[appId] : null);
        }

        public Task<bool> IsAuthenticationDisabledAsync()
        {
            return Task.FromResult(!this.Credentials.Any());
        }
    }
}
