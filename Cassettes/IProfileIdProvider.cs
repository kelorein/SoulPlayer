using System;
using Comfort.Common;
using EFT;

namespace SoulPlayer.Cassettes
{
    internal interface IProfileIdProvider
    {
        string GetActiveProfileId(string fallbackProfileId);
    }

    internal sealed class SptProfileIdProvider : IProfileIdProvider
    {
        private readonly ISoulTapeLog _log;
        private readonly Func<SptProfileResolutionSnapshot> _capture;
        private bool _loggedApplicationUnavailable;
        private bool _loggedBackendSessionUnavailable;
        private bool _loggedBackendProfileUnavailable;
        private bool _loggedBackendProfileIdUnavailable;
        private bool _loggedResolutionException;
        private string _lastResolvedProfile = string.Empty;

        internal SptProfileIdProvider(ISoulTapeLog log)
            : this(log, CaptureSptProfileResolution)
        {
        }

        internal SptProfileIdProvider(
            ISoulTapeLog log,
            Func<SptProfileResolutionSnapshot> capture)
        {
            _log = log ?? throw new ArgumentNullException("log");
            _capture = capture ?? throw new ArgumentNullException("capture");
        }

        public string GetActiveProfileId(string fallbackProfileId)
        {
            try
            {
                SptProfileResolutionSnapshot snapshot = _capture();
                _loggedResolutionException = false;

                if (!snapshot.ApplicationAvailable)
                {
                    LogWarningOnce(
                        ref _loggedApplicationUnavailable,
                        "SoulTape profile binding is waiting because TarkovApplication is unavailable.");
                    return Resolve(fallbackProfileId, "raid player fallback");
                }

                _loggedApplicationUnavailable = false;

                if (!snapshot.BackendSessionAvailable)
                {
                    LogWarningOnce(
                        ref _loggedBackendSessionUnavailable,
                        "SoulTape profile binding is waiting because the client backend session is unavailable.");
                }
                else
                {
                    _loggedBackendSessionUnavailable = false;
                    if (!snapshot.BackendProfileAvailable)
                    {
                        LogWarningOnce(
                            ref _loggedBackendProfileUnavailable,
                            "SoulTape profile binding is waiting because the client backend Profile is unavailable.");
                    }
                    else
                    {
                        _loggedBackendProfileUnavailable = false;
                        if (!string.IsNullOrWhiteSpace(snapshot.BackendProfileId))
                        {
                            _loggedBackendProfileIdUnavailable = false;
                            return Resolve(snapshot.BackendProfileId, "client backend session");
                        }

                        LogWarningOnce(
                            ref _loggedBackendProfileIdUnavailable,
                            "SoulTape profile binding is waiting because the client backend ProfileId is unavailable.");
                    }
                }

                if (!string.IsNullOrWhiteSpace(snapshot.SessionProfileId))
                {
                    return Resolve(snapshot.SessionProfileId, "TarkovApplication.Session fallback");
                }
            }
            catch (Exception ex)
            {
                LogWarningOnce(
                    ref _loggedResolutionException,
                    "SoulTape could not inspect the active SPT profile yet: " + ex.Message);
            }

            return Resolve(fallbackProfileId, "raid player fallback");
        }

        private static SptProfileResolutionSnapshot CaptureSptProfileResolution()
        {
            TarkovApplication application = Singleton<TarkovApplication>.Instance;
            if (application == null)
            {
                return SptProfileResolutionSnapshot.ApplicationUnavailable;
            }

            IEftSession backendSession = application.GetClientBackEndSession();
            Profile backendProfile = backendSession == null ? null : backendSession.Profile;

            IProfileSession profileSession = application.Session as IProfileSession;
            Profile sessionProfile = profileSession == null ? null : profileSession.Profile;

            return new SptProfileResolutionSnapshot(
                true,
                backendSession != null,
                backendProfile != null,
                backendProfile == null ? string.Empty : backendProfile.ProfileId,
                sessionProfile == null ? string.Empty : sessionProfile.ProfileId);
        }

        private string Resolve(string profileId, string source)
        {
            string resolved = profileId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(resolved))
            {
                return string.Empty;
            }

            string resolutionKey = source + ":" + resolved;
            if (!string.Equals(_lastResolvedProfile, resolutionKey, StringComparison.Ordinal))
            {
                _lastResolvedProfile = resolutionKey;
                _log.Info(
                    "SoulTape profile binding resolved from " + source +
                    " for profile " + resolved + ".");
            }

            return resolved;
        }

        private void LogWarningOnce(ref bool alreadyLogged, string message)
        {
            if (alreadyLogged)
            {
                return;
            }

            alreadyLogged = true;
            _log.Warning(message);
        }
    }

    internal sealed class SptProfileResolutionSnapshot
    {
        internal static readonly SptProfileResolutionSnapshot ApplicationUnavailable =
            new SptProfileResolutionSnapshot(false, false, false, string.Empty, string.Empty);

        internal SptProfileResolutionSnapshot(
            bool applicationAvailable,
            bool backendSessionAvailable,
            bool backendProfileAvailable,
            string backendProfileId,
            string sessionProfileId)
        {
            ApplicationAvailable = applicationAvailable;
            BackendSessionAvailable = backendSessionAvailable;
            BackendProfileAvailable = backendProfileAvailable;
            BackendProfileId = backendProfileId ?? string.Empty;
            SessionProfileId = sessionProfileId ?? string.Empty;
        }

        internal bool ApplicationAvailable { get; private set; }
        internal bool BackendSessionAvailable { get; private set; }
        internal bool BackendProfileAvailable { get; private set; }
        internal string BackendProfileId { get; private set; }
        internal string SessionProfileId { get; private set; }
    }
}
