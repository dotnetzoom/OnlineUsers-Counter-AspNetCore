using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;

namespace OnlineUsers
{
    public static class OnlineUsersMiddlewareExtensions
    {
        public static void UseOnlineUsers(this IApplicationBuilder app, string cookieName = "UserGuid", int lastActivityMinutes = 20)
        {
            app.UseMiddleware<OnlineUsersMiddleware>(cookieName, lastActivityMinutes);
        }
    }

    public class OnlineUsersMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly string _cookieName;
        private readonly int _lastActivityMinutes = 20;
        private static readonly ConcurrentDictionary<string, bool> _allKeys = new ConcurrentDictionary<string, bool>();

        public OnlineUsersMiddleware(RequestDelegate next, string cookieName = "UserGuid", int lastActivityMinutes = 20)
        {
            _next = next;
            _cookieName = cookieName;
            _lastActivityMinutes = lastActivityMinutes;
        }

        public Task InvokeAsync(HttpContext context, IMemoryCache memoryCache)
        {
            if (context.Request.Cookies.TryGetValue(_cookieName, out var userGuid) == false)
            {
                userGuid = Guid.NewGuid().ToString();
                context.Response.Cookies.Append(_cookieName, userGuid, new CookieOptions { HttpOnly = true, MaxAge = TimeSpan.FromDays(30) });
            }

            memoryCache.GetOrCreate(userGuid, cacheEntry =>
            {
                if (_allKeys.TryAdd(userGuid, true) == false)
                {
                    //if add key faild, setting expiration to the past cause dose not cache
                    cacheEntry.AbsoluteExpiration = DateTimeOffset.MinValue;
                }
                else
                {
                    cacheEntry.SlidingExpiration = TimeSpan.FromMinutes(_lastActivityMinutes);
                    cacheEntry.RegisterPostEvictionCallback(RemoveKeyWhenExpired);
                }
                return string.Empty;
            });

            return _next(context);
        }

        private void RemoveKeyWhenExpired(object key, object value, EvictionReason reason, object state)
        {
            var strKey = (string)key;
            //try to remove key from dictionary
            if (!_allKeys.TryRemove(strKey, out _))
                //if not possible to remove key from dictionary, then try to mark key as not existing in cache
                _allKeys.TryUpdate(strKey, false, true);
        }

        public static int GetOnlineUsersCount()
        {
            return _allKeys.Count(p => p.Value);
        }
    }
}
