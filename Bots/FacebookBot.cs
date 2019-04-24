// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Microsoft.Bot.Connector;
using Microsoft.Bot.Schema;
using Microsoft.BotBuilderSamples.FacebookModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.BotBuilderSamples.Bots
{
    public class FacebookBot : ActivityHandler
    {
        // These are the options provided to the user when they message the bot
        const string FacebookPageIdOption = "Facebook Id";
        const string QuickRepliesOption = "Quick Replies";
        const string PostBackOption = "PostBack";
        const string EscalateOption = "Escalate";
        const string PrimaryBotOption = "Primary";

        protected readonly ILogger Logger;
        private readonly IConfiguration _configuration;

        public FacebookBot(ILogger<FacebookBot> logger, IConfiguration configuration)
        {
            Logger = logger;
            _configuration = configuration;
        }

        protected override async Task OnMessageActivityAsync(ITurnContext<IMessageActivity> turnContext, CancellationToken cancellationToken)
        {
            Logger.LogInformation("Processing a Message Activity.");

            if (turnContext.Activity.Text?.Equals(EscalateOption, StringComparison.InvariantCultureIgnoreCase) == true)
            {
                await FacebookThreadControlHelper.RequestThreadControlToBot(turnContext, _configuration["SecondaryPageToken"], turnContext.Activity.From.Id, EscalateOption);
            }
            else if (turnContext.Activity.Text?.Equals(PrimaryBotOption, StringComparison.InvariantCultureIgnoreCase) == true)
            {
                await FacebookThreadControlHelper.PassThreadControlToPrimaryBot(turnContext, _configuration["PrimaryPageToken"], turnContext.Activity.From.Id, PrimaryBotOption);
            }
            else
            {
                // Show choices if the Facebook Payload from ChannelData is not handled
                if (!await ProcessFacebookPayload(turnContext, turnContext.Activity.ChannelData, cancellationToken))
                    await ShowChoices(turnContext, cancellationToken);
            }
        }

        protected override async Task OnConversationUpdateActivityAsync(ITurnContext<IConversationUpdateActivity> turnContext, CancellationToken cancellationToken)
        {
            Logger.LogInformation("Processing a ConversationUpdate Activity.");

            var facebookPayload = (turnContext.Activity.ChannelData as JObject)?.ToObject<FacebookPayload>();
            if (facebookPayload != null)
            {
                if (facebookPayload.PassThreadControl != null) {
                    await turnContext.SendActivityAsync($"Thread control is now passed to: {facebookPayload.PassThreadControl.RequestOwnerAppId} with message: {facebookPayload.PassThreadControl.Metadata}");
                    await ShowChoices(turnContext, cancellationToken);
                }
                else if (facebookPayload.TakeThreadControl != null)
                {
                    await turnContext.SendActivityAsync($"Thread control is now passed to Primary.  Previous thread owner: {facebookPayload.TakeThreadControl.PreviousOwnerAppId} with message: {facebookPayload.TakeThreadControl.Metadata}");
                    await ShowChoices(turnContext, cancellationToken);
                }
            }

            await base.OnConversationUpdateActivityAsync(turnContext, cancellationToken);
        }

        protected override async Task OnEventActivityAsync(ITurnContext<IEventActivity> turnContext, CancellationToken cancellationToken)
        {
            Logger.LogInformation("Processing an Event Activity.");
            
            // Analyze Facebook payload from EventActivity.Value
            await ProcessFacebookMessage(turnContext, turnContext.Activity.Value, cancellationToken);
        }

        private static async Task ShowChoices(ITurnContext turnContext, CancellationToken cancellationToken)
        {
            // Create choices for the prompt
            var choices = new List<Choice>();
            choices.Add(new Choice() { Value = QuickRepliesOption, Action = new CardAction() { Title = QuickRepliesOption, Type = ActionTypes.PostBack, Value = QuickRepliesOption } });
            choices.Add(new Choice() { Value = FacebookPageIdOption, Action = new CardAction() { Title = FacebookPageIdOption, Type = ActionTypes.PostBack, Value = FacebookPageIdOption } });
            choices.Add(new Choice() { Value = PostBackOption, Action = new CardAction() { Title = PostBackOption, Type = ActionTypes.PostBack, Value = PostBackOption } });
            choices.Add(new Choice() { Value = EscalateOption, Action = new CardAction() { Title = EscalateOption, Type = ActionTypes.PostBack, Value = EscalateOption } });
            choices.Add(new Choice() { Value = PrimaryBotOption, Action = new CardAction() { Title = PrimaryBotOption, Type = ActionTypes.PostBack, Value = PrimaryBotOption } });

            // Create the prompt message
            var message = ChoiceFactory.ForChannel(turnContext.Activity.ChannelId, choices, "What Facebook feature would you like to try? Here are some quick replies to choose from!");
            await turnContext.SendActivityAsync(message, cancellationToken);
        }

        private async Task<bool> ProcessFacebookMessage(ITurnContext turnContext, object data, CancellationToken cancellationToken)
        {
            return await ProcessStandbyPayload(turnContext, data, cancellationToken)
                || await ProcessFacebookPayload(turnContext, data, cancellationToken);
        }

        private async Task<bool> ProcessFacebookPayload(ITurnContext turnContext, object data, CancellationToken cancellationToken)
        {
            try
            {
                var facebookPayload = (data as JObject)?.ToObject<FacebookPayload>();
                if (facebookPayload != null)
                {
                    // At this point we know we are on Facebook channel, and can consume the Facebook custom payload
                    // present in channelData.

                    // PostBack
                    if (facebookPayload.PostBack != null)
                    {
                        await OnFacebookPostBack(turnContext, facebookPayload.PostBack, cancellationToken);
                        return true;
                    }

                    // Optin
                    else if (facebookPayload.Optin != null)
                    {
                        await OnFacebookOptin(turnContext, facebookPayload.Optin, cancellationToken);
                        return true;
                    }

                    // Quick reply
                    else if (facebookPayload.Message?.QuickReply != null)
                    {
                        await OnFacebookQuickReply(turnContext, facebookPayload.Message.QuickReply, cancellationToken);
                        return true;
                    }

                    // Echo
                    else if (facebookPayload.Message?.IsEcho != null && facebookPayload.Message.IsEcho)
                    {
                        await OnFacebookEcho(turnContext, facebookPayload.Message, cancellationToken);
                        return true;
                    }

                    // Thread Control Request
                    else if (facebookPayload.RequestThreadControl != null)
                    {
                        await OnFacebookThreadControlRequest(turnContext, facebookPayload, cancellationToken);
                        return true;
                    }
                    // TODO: Handle other events that you're interested in...
                }
            }
            catch (Newtonsoft.Json.JsonSerializationException e)
            {
                if (turnContext.Activity.ChannelId != Channels.Facebook)
                {
                    await turnContext.SendActivityAsync("This sample is intended to be used with a Facebook bot.");
                }
            }
            return false;
        }

        private async Task<bool> ProcessStandbyPayload(ITurnContext turnContext, object data, CancellationToken cancellationToken)
        {
            if (turnContext.Activity.Name?.Equals("standby", StringComparison.InvariantCultureIgnoreCase) == true)
            {
                var standbys = (data as JObject)?.ToObject<FacebookStandbys>();
                if (standbys != null)
                {
                    foreach (var standby in standbys.Standbys)
                    {
                        await OnFacebookStandby(turnContext, standby, cancellationToken);
                        return true;
                    }
                }
            }
            return false;
        }
        
        protected virtual async Task OnFacebookThreadControlRequest(ITurnContext turnContext, FacebookPayload facebookPayload, CancellationToken cancellationToken)
        {
            Logger.LogInformation("Thread Control Request message received.");

            // TODO: Your Thread Control Request event handling logic here...

            if (facebookPayload.RequestThreadControl.RequestOwnerAppId == _configuration["SecondaryFacebookAppId"])
            {
                await FacebookThreadControlHelper.PassThreadControlToBot(turnContext, _configuration["PrimaryPageToken"], facebookPayload.RequestThreadControl.RequestOwnerAppId, facebookPayload.Sender.Id, "allowing thread control");
            }
            else
            {
                Logger.LogInformation($"Thread Control Request denied.  Only allowed for {_configuration["SecondaryFacebookAppId"]}");
            }
        }

        protected virtual async Task OnFacebookStandby(ITurnContext turnContext, FacebookStandby facebookStandby, CancellationToken cancellationToken)
        {
            Logger.LogInformation("Standby message received.");

            // TODO: Your echo event handling logic here...

        }

        protected virtual async Task OnFacebookOptin(ITurnContext turnContext, FacebookOptin optin, CancellationToken cancellationToken)
        {
            Logger.LogInformation("Optin message received.");

            // TODO: Your optin event handling logic here...
        }

        protected virtual async Task OnFacebookEcho(ITurnContext turnContext, FacebookMessage facebookMessage, CancellationToken cancellationToken)
        {
            Logger.LogInformation("Echo message received.");

            // TODO: Your echo event handling logic here...
        }

        protected virtual async Task OnFacebookPostBack(ITurnContext turnContext, FacebookPostback postBack, CancellationToken cancellationToken)
        {
            Logger.LogInformation("PostBack message received.");

            // TODO: Your PostBack handling logic here...

            // Answer the postback, and show choices
            var reply = turnContext.Activity.CreateReply($"Are you sure?");
            await turnContext.SendActivityAsync(reply, cancellationToken);
            await ShowChoices(turnContext, cancellationToken);
        }

        protected virtual async Task OnFacebookQuickReply(ITurnContext turnContext, FacebookQuickReply quickReply, CancellationToken cancellationToken)
        {
            Logger.LogInformation("QuickReply message received.");

            // TODO: Your quick reply event handling logic here...

            // Process the message by checking the Activity.Text.  The FacebookQuickReply could also contain a json payload.

            // Initially the bot offers to showcase 3 Facebook features: Quick replies, PostBack and getting the Facebook Page Name.
            switch (turnContext.Activity.Text)
            {
                // Here we showcase how to obtain the Facebook page id.
                // This can be useful for the Facebook multi-page support provided by the Bot Framework.
                // The Facebook page id from which the message comes from is in turnContext.Activity.Recipient.Id.
                case FacebookPageIdOption:
                    {
                        var reply = turnContext.Activity.CreateReply($"This message comes from the following Facebook Page: {turnContext.Activity.Recipient.Id} for the following Bot: {turnContext.Activity.Recipient.Name}");
                        await turnContext.SendActivityAsync(reply, cancellationToken);
                        await ShowChoices(turnContext, cancellationToken);

                        break;
                    }

                // Here we send a HeroCard with 2 options that will trigger a Facebook PostBack.
                case PostBackOption:
                    {
                        var card = new HeroCard
                        {
                            Text = "Is 42 the answer to the ultimate question of Life, the Universe, and Everything?",
                            Buttons = new List<CardAction>
                                    {
                                        new CardAction() { Title = "Yes", Type = ActionTypes.PostBack, Value = "Yes" },
                                        new CardAction() { Title = "No", Type = ActionTypes.PostBack, Value = "No" },
                                    },
                        };

                        var reply = turnContext.Activity.CreateReply();
                        reply.Attachments = new List<Attachment> { card.ToAttachment() };
                        await turnContext.SendActivityAsync(reply, cancellationToken);

                        break;
                    }

                case EscalateOption:
                    {
                        await turnContext.SendActivityAsync("Requesting thread control for Secondary bot");
                        await FacebookThreadControlHelper.RequestThreadControlToBot(turnContext, _configuration["SecondaryPageToken"], turnContext.Activity.From.Id, EscalateOption);
                        break;
                    }

                case PrimaryBotOption:
                    {
                        await turnContext.SendActivityAsync("Passing thread control to Primary bot");
                        await FacebookThreadControlHelper.PassThreadControlToPrimaryBot(turnContext, _configuration["PrimaryPageToken"], turnContext.Activity.From.Id, PrimaryBotOption);

                        break;
                    }

                // By default we offer the users different actions that the bot supports, through quick replies.
                case QuickRepliesOption:
                default:
                    {
                        await ShowChoices(turnContext, cancellationToken);

                        break;
                    }
            }
        }
    }
}
