using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace DfoGmTool.Services
{
    // Keeps remote access policy at the host boundary instead of in GM services.
    public sealed class GmAccessControl
    {
        private const string SessionCookieName = "dfo_gm_session";
        private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);

        private readonly bool _requiresAuthentication;
        private readonly byte[] _passwordHash;
        private readonly ConcurrentDictionary<string, Session> _sessions = new ConcurrentDictionary<string, Session>();

        public GmAccessControl(GmToolHostConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            _requiresAuthentication = config.AllowRemoteAccess;
            _passwordHash = _requiresAuthentication
                ? SHA256.HashData(Encoding.UTF8.GetBytes(config.RemotePassword))
                : Array.Empty<byte>();
        }

        public bool RequiresAuthentication => _requiresAuthentication;

        public bool IsAuthenticated(HttpContext context)
        {
            if (!_requiresAuthentication)
                return true;
            if (context == null || !context.Request.Cookies.TryGetValue(SessionCookieName, out var token))
                return false;
            if (!_sessions.TryGetValue(token, out var session))
                return false;
            if (session.ExpiresAt > DateTimeOffset.UtcNow)
                return true;

            _sessions.TryRemove(token, out _);
            return false;
        }

        public object Login(HttpContext context, string password)
        {
            if (!_requiresAuthentication)
                return new { success = true, authenticated = true };

            var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(password ?? string.Empty));
            if (!CryptographicOperations.FixedTimeEquals(_passwordHash, suppliedHash))
                return new { success = false, error = "密码错误。" };

            RemoveExpiredSessions();
            var expiresAt = DateTimeOffset.UtcNow.Add(SessionLifetime);
            var token = CreateSessionToken();
            _sessions[token] = new Session(expiresAt);
            context.Response.Cookies.Append(SessionCookieName, token, new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Strict,
                Secure = context.Request.IsHttps,
                Path = "/",
                Expires = expiresAt,
                MaxAge = SessionLifetime,
            });

            return new { success = true, authenticated = true };
        }

        public void Logout(HttpContext context)
        {
            if (context != null && context.Request.Cookies.TryGetValue(SessionCookieName, out var token))
                _sessions.TryRemove(token, out _);

            context?.Response.Cookies.Delete(SessionCookieName, new CookieOptions { Path = "/" });
        }

        private void RemoveExpiredSessions()
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var pair in _sessions)
            {
                if (pair.Value.ExpiresAt <= now)
                    _sessions.TryRemove(pair.Key, out _);
            }
        }

        private static string CreateSessionToken()
        {
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private sealed class Session
        {
            public Session(DateTimeOffset expiresAt)
            {
                ExpiresAt = expiresAt;
            }

            public DateTimeOffset ExpiresAt { get; }
        }
    }
}
