﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Octokit;

using Tgstation.Server.Host.Configuration;
using Tgstation.Server.Host.Extensions;
using Tgstation.Server.Host.IO;
using Tgstation.Server.Host.Swarm;

namespace Tgstation.Server.Host.Core
{
	/// <inheritdoc />
	sealed class ServerUpdater : IServerUpdater, IServerUpdateExecutor
	{
		/// <summary>
		/// The <see cref="IGitHubClientFactory"/> for the <see cref="ServerUpdater"/>.
		/// </summary>
		readonly IGitHubClientFactory gitHubClientFactory;

		/// <summary>
		/// The <see cref="IIOManager"/> for the <see cref="ServerUpdater"/>.
		/// </summary>
		readonly IIOManager ioManager;

		/// <summary>
		/// The <see cref="IServerControl"/> for the <see cref="ServerUpdater"/>.
		/// </summary>
		readonly IServerControl serverControl;

		/// <summary>
		/// The <see cref="ILogger"/> for the <see cref="ServerUpdater"/>.
		/// </summary>
		readonly ILogger<ServerUpdater> logger;

		/// <summary>
		/// The <see cref="UpdatesConfiguration"/> for the <see cref="ServerUpdater"/>.
		/// </summary>
		readonly UpdatesConfiguration updatesConfiguration;

		/// <summary>
		/// <see cref="ServerUpdateOperation"/> for an in-progress update operation.
		/// </summary>
		ServerUpdateOperation serverUpdateOperation;

		/// <summary>
		/// Initializes a new instance of the <see cref="ServerUpdater"/> class.
		/// </summary>
		/// <param name="gitHubClientFactory">The value of <see cref="gitHubClientFactory"/>.</param>
		/// <param name="ioManager">The value of <see cref="ioManager"/>.</param>
		/// <param name="serverControl">The value of <see cref="serverControl"/>.</param>
		/// <param name="logger">The value of <see cref="logger"/>.</param>
		/// <param name="updatesConfigurationOptions">The <see cref="IOptions{TOptions}"/> containing the value of <see cref="updatesConfiguration"/>.</param>
		public ServerUpdater(
			IGitHubClientFactory gitHubClientFactory,
			IIOManager ioManager,
			IServerControl serverControl,
			ILogger<ServerUpdater> logger,
			IOptions<UpdatesConfiguration> updatesConfigurationOptions)
		{
			this.gitHubClientFactory = gitHubClientFactory ?? throw new ArgumentNullException(nameof(gitHubClientFactory));
			this.ioManager = ioManager ?? throw new ArgumentNullException(nameof(ioManager));
			this.serverControl = serverControl ?? throw new ArgumentNullException(nameof(serverControl));
			this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
			updatesConfiguration = updatesConfigurationOptions?.Value ?? throw new ArgumentNullException(nameof(updatesConfigurationOptions));
		}

		/// <inheritdoc />
		public async Task<ServerUpdateResult> BeginUpdate(ISwarmService swarmService, Version newVersion, CancellationToken cancellationToken)
		{
			if (swarmService == null)
				throw new ArgumentNullException(nameof(swarmService));

			if (newVersion == null)
				throw new ArgumentNullException(nameof(newVersion));

			logger.LogDebug("Looking for GitHub releases version {version}...", newVersion);
			IEnumerable<Release> releases;
			var gitHubClient = gitHubClientFactory.CreateClient();
			releases = await gitHubClient
				.Repository
				.Release
				.GetAll(updatesConfiguration.GitHubRepositoryId)
				.WithToken(cancellationToken);

			releases = releases.Where(x => x.TagName.StartsWith(updatesConfiguration.GitTagPrefix, StringComparison.InvariantCulture));

			logger.LogTrace("Release query complete!");

			foreach (var release in releases)
				if (Version.TryParse(
					release.TagName.Replace(
						updatesConfiguration.GitTagPrefix, String.Empty, StringComparison.Ordinal),
					out var version)
					&& version == newVersion)
				{
					var asset = release.Assets.Where(x => x.Name.Equals(updatesConfiguration.UpdatePackageAssetName, StringComparison.Ordinal)).FirstOrDefault();
					if (asset == default)
						continue;

					serverUpdateOperation = new ServerUpdateOperation
					{
						TargetVersion = version,
						UpdateZipUrl = new Uri(asset.BrowserDownloadUrl),
						SwarmService = swarmService,
					};

					try
					{
						if (!serverControl.TryStartUpdate(this, version))
							return ServerUpdateResult.UpdateInProgress;
					}
					finally
					{
						serverUpdateOperation = null;
					}

					return ServerUpdateResult.Started;
				}

			return ServerUpdateResult.ReleaseMissing;
		}

		/// <inheritdoc />
		public async Task<bool> ExecuteUpdate(string updatePath, CancellationToken cancellationToken, CancellationToken criticalCancellationToken)
		{
			if (updatePath == null)
				throw new ArgumentNullException(nameof(updatePath));

			var serverUpdateOperation = this.serverUpdateOperation;
			if (serverUpdateOperation == null)
				throw new InvalidOperationException($"{nameof(serverUpdateOperation)} was null!");

			logger.LogInformation(
				"Updating server to version {version} ({zipUrl})...",
				serverUpdateOperation.TargetVersion,
				serverUpdateOperation.UpdateZipUrl);

			var inMustCommitUpdate = false;
			try
			{
				var updatePrepareResult = await serverUpdateOperation.SwarmService.PrepareUpdate(serverUpdateOperation.TargetVersion, cancellationToken);
				if (!updatePrepareResult)
					return false;

				MemoryStream updateZipData;
				try
				{
					logger.LogTrace("Downloading zip package...");
					updateZipData = await ioManager.DownloadFile(serverUpdateOperation.UpdateZipUrl, cancellationToken);
				}
				catch (Exception e1)
				{
					try
					{
						await serverUpdateOperation.SwarmService.AbortUpdate(cancellationToken);
					}
					catch (Exception e2)
					{
						throw new AggregateException(e1, e2);
					}

					throw;
				}

				using (updateZipData)
				{
					var updateCommitResult = await serverUpdateOperation.SwarmService.CommitUpdate(criticalCancellationToken);
					if (updateCommitResult == SwarmCommitResult.AbortUpdate)
					{
						logger.LogError("Swarm distributed commit failed, not applying update!");
						return false;
					}

					inMustCommitUpdate = updateCommitResult == SwarmCommitResult.MustCommitUpdate;
					try
					{
						logger.LogTrace("Extracting zip package to {extractPath}...", updatePath);
						await ioManager.ZipToDirectory(updatePath, updateZipData, criticalCancellationToken);
					}
					catch (Exception e)
					{
						try
						{
							// important to not leave this directory around if possible
							await ioManager.DeleteDirectory(updatePath, default);
						}
						catch (Exception e2)
						{
							throw new AggregateException(e, e2);
						}

						throw;
					}
				}

				return true;
			}
			catch (OperationCanceledException) when (!inMustCommitUpdate)
			{
				logger.LogInformation("Server update cancelled!");
			}
			catch (Exception ex) when (!inMustCommitUpdate)
			{
				logger.LogError(ex, "Error updating server!");
			}
			catch (Exception ex) when (inMustCommitUpdate)
			{
				logger.LogCritical(ex, "Could not complete committed swarm update!");
			}

			return false;
		}
	}
}
