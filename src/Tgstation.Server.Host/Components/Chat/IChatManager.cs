﻿using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Tgstation.Server.Api.Models.Internal;

namespace Tgstation.Server.Host.Components.Chat
{
	/// <summary>
	/// For managing connected chat services
	/// </summary>
	public interface IChatManager : IHostedService, IDisposable
	{
		/// <summary>
		/// Registers a <paramref name="customCommandHandler"/> to use
		/// </summary>
		/// <param name="customCommandHandler">A <see cref="ICustomCommandHandler"/></param>
		void RegisterCommandHandler(ICustomCommandHandler customCommandHandler);

		/// <summary>
		/// Change chat settings. If the <see cref="ChatBot.Id"/> is not currently in use, a new connection will be made instead
		/// </summary>
		/// <param name="newSettings">The new <see cref="ChatBot"/></param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task"/> representing the running operation. Will complete immediately if the <see cref="ChatBot.Enabled"/> property of <paramref name="newSettings"/> is <see langword="false"/></returns>
		Task ChangeSettings(ChatBot newSettings, CancellationToken cancellationToken);

		/// <summary>
		/// Disconnects and deletes a given connection
		/// </summary>
		/// <param name="connectionId">The <see cref="ChatBot.Id"/> of the connection</param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task"/> representing the running operation</returns>
		Task DeleteConnection(long connectionId, CancellationToken cancellationToken);

		/// <summary>
		/// Change chat channels
		/// </summary>
		/// <param name="connectionId">The <see cref="ChatBot.Id"/> of the connection</param>
		/// <param name="newChannels">An <see cref="IEnumerable{T}"/> of the new list of <see cref="Api.Models.ChatChannel"/>s</param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task"/> representing the running operation</returns>
		Task ChangeChannels(long connectionId, IEnumerable<Api.Models.ChatChannel> newChannels, CancellationToken cancellationToken);

		/// <summary>
		/// Send a chat <paramref name="message"/> to a given set of <paramref name="channelIds"/>
		/// </summary>
		/// <param name="message">The message being sent</param>
		/// <param name="channelIds">The <see cref="Models.ChatChannel.Id"/>s of the <see cref="Models.ChatChannel"/>s to send to</param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task"/> representing the running operation</returns>
		Task SendMessage(string message, IEnumerable<ulong> channelIds, CancellationToken cancellationToken);

		/// <summary>
		/// Send a chat <paramref name="message"/> to configured watchdog channels
		/// </summary>
		/// <param name="message">The message being sent</param>
		/// <param name="adminOnly">If the message should be sent to admin channels only. If no admin-only channels are configured, it will be sent to the watchdog channels.</param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task"/> representing the running operation</returns>
		Task SendWatchdogMessage(string message, bool adminOnly, CancellationToken cancellationToken);

		/// <summary>
		/// Send a chat <paramref name="message"/> to configured update channels
		/// </summary>
		/// <param name="message">The message being sent</param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task"/> representing the running operation</returns>
		Task SendUpdateMessage(string message, CancellationToken cancellationToken);

		/// <summary>
		/// Start tracking <see cref="Commands.CustomCommand"/>s and <see cref="ChannelRepresentation"/>s.
		/// </summary>
		/// <returns>A new <see cref="IChatTrackingContext"/>.</returns>
		IChatTrackingContext CreateTrackingContext();
	}
}