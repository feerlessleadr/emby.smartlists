using System;
using Emby.Plugin.SmartLists.Host;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Emby.Plugin.SmartLists.Services.Shared
{
    /// <summary>
    /// Scheduled task that creates automated backups of SmartLists.
    /// Creates timestamped ZIP archives containing all smart list configurations and images.
    /// </summary>
    public class BackupTask : IScheduledTask
    {
        // Emby instantiates scheduled tasks itself, so the services come from the host on use.
        private static SmartListsHost Host => SmartListsHost.Instance
            ?? throw new InvalidOperationException("SmartLists is not running yet");

        private static IBackupService BackupService => Host.BackupService;
        private static ILogger<BackupTask> Logger => Host.CreateLogger<BackupTask>();

        /// <summary>
        /// Gets the name of the task.
        /// </summary>
        public string Name => "SmartLists backup task";

        /// <summary>
        /// Gets the key of the task.
        /// </summary>
        public string Key => "SmartListsBackup";

        /// <summary>
        /// Gets the description of the task.
        /// </summary>
        public string Description => "Creates automated backups of all SmartLists configurations and images.";

        /// <summary>
        /// Gets the category of the task.
        /// </summary>
        public string Category => "SmartLists";

        /// <summary>
        /// Gets the default triggers for this task.
        /// Runs daily at 3:00 AM by default. Schedule can be changed in Emby's Scheduled Tasks.
        /// </summary>
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = "DailyTrigger",
                    TimeOfDayTicks = TimeSpan.FromHours(3).Ticks
                }
            };
        }

        /// <summary>
        /// Executes the backup task.
        /// </summary>
        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            var config = Plugin.Instance?.Configuration;

            // Check if backups are enabled
            if (config == null || !config.BackupEnabled)
            {
                Logger.LogDebug("SmartLists backup is disabled, skipping");
                progress.Report(100);
                return;
            }

            Logger.LogInformation("Starting SmartLists backup task");

            try
            {
                progress.Report(10);

                // Create backup using the service
                var result = await BackupService.CreateBackupAsync(cancellationToken).ConfigureAwait(false);

                if (!result.Success)
                {
                    Logger.LogError("Backup task failed: {ErrorMessage}", result.ErrorMessage);
                    throw new InvalidOperationException(result.ErrorMessage);
                }

                progress.Report(80);

                // Cleanup old backups
                BackupService.CleanupOldBackups(config.BackupRetentionCount, cancellationToken);

                progress.Report(100);
                Logger.LogInformation("SmartLists backup completed: {BackupFile} ({ListCount} lists)", result.Filename, result.ListCount);
            }
            catch (OperationCanceledException)
            {
                Logger.LogInformation("SmartLists backup task was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error during SmartLists backup task");
                throw;
            }
        }
    }
}
