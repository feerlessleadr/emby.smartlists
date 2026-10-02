using System;
using MediaBrowser.Model.Logging;
using Microsoft.Extensions.Logging;

namespace Emby.Plugin.SmartLists.Host
{
    /// <summary>
    /// Bridges <see cref="Microsoft.Extensions.Logging"/> (used throughout the plugin code) to Emby's own logger, so
    /// plugin messages land in Emby's server log. Emby's <see cref="MediaBrowser.Model.Logging.ILogger"/> is a different
    /// interface from <c>Microsoft.Extensions.Logging.ILogger</c>.
    /// </summary>
    public sealed class EmbyLoggerProvider : ILoggerProvider
    {
        private readonly ILogManager _logManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="EmbyLoggerProvider"/> class.
        /// </summary>
        /// <param name="logManager">Emby's log manager.</param>
        public EmbyLoggerProvider(ILogManager logManager)
        {
            _logManager = logManager ?? throw new ArgumentNullException(nameof(logManager));
        }

        /// <inheritdoc />
        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName)
            => new EmbyLogger(_logManager.GetLogger("SmartLists"), categoryName);

        /// <inheritdoc />
        public void Dispose()
        {
        }

        private sealed class EmbyLogger : Microsoft.Extensions.Logging.ILogger
        {
            private readonly MediaBrowser.Model.Logging.ILogger _inner;
            private readonly string _category;

            public EmbyLogger(MediaBrowser.Model.Logging.ILogger inner, string category)
            {
                _inner = inner;

                // Keep just the class name: "Emby.Plugin.SmartLists.Services.Shared.RefreshQueueService" -> "RefreshQueueService".
                var dot = category.LastIndexOf('.');
                _category = dot >= 0 ? category[(dot + 1)..] : category;
            }

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }

                var text = _category + ": " + formatter(state, exception);

                // Always pass the text as a format argument so braces in it are not treated as a format string.
                switch (logLevel)
                {
                    case LogLevel.Trace:
                    case LogLevel.Debug:
                        _inner.Debug("{0}", text);
                        break;
                    case LogLevel.Information:
                        _inner.Info("{0}", text);
                        break;
                    case LogLevel.Warning:
                        _inner.Warn("{0}", exception == null ? text : text + " | " + exception.GetType().Name + ": " + exception.Message);
                        break;
                    case LogLevel.Error:
                        if (exception != null)
                        {
                            _inner.ErrorException("{0}", exception, text);
                        }
                        else
                        {
                            _inner.Error("{0}", text);
                        }

                        break;
                    default:
                        if (exception != null)
                        {
                            _inner.FatalException("{0}", exception, text);
                        }
                        else
                        {
                            _inner.Fatal("{0}", text);
                        }

                        break;
                }
            }
        }
    }
}
