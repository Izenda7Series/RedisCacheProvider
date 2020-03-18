using Izenda.BI.CacheProvider.RedisCache.Serializer;
using Izenda.BI.Framework.Converters;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Composition;

namespace Izenda.BI.CacheProvider.RedisCache
{
    /// <summary>
    /// Redis cache provider
    /// </summary>
    [Export(typeof(ICacheProvider))]
    public class RedisCacheProvider : ICacheProvider, IDisposable
    {
        private readonly RedisCache RedisCache;
        private bool _disposed = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="RedisCacheProvider"/> class
        /// </summary>
        public RedisCacheProvider()
        {
            RedisCache = new RedisCache(new JsonSerializerSettings
            {
                DateTimeZoneHandling = DateTimeZoneHandling.Unspecified,
                NullValueHandling = NullValueHandling.Ignore,
                ObjectCreationHandling = ObjectCreationHandling.Replace,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                TypeNameHandling = TypeNameHandling.Objects,
                Converters = new List<JsonConverter> { new ReportPartContentConverter(), new DBServerTypeSupportingConverter() }
            });
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RedisCacheProvider"/> class
        /// </summary>
        /// <param name="cache">The redis database cache</param>
        public RedisCacheProvider(StackExchange.Redis.IDatabase cache)
        {
            RedisCache = new RedisCache(cache, new JsonSerializerSettings
            {
                DateTimeZoneHandling = DateTimeZoneHandling.Unspecified,
                NullValueHandling = NullValueHandling.Ignore,
                ObjectCreationHandling = ObjectCreationHandling.Replace,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                TypeNameHandling = TypeNameHandling.Objects,
                Converters = new List<JsonConverter> { new ReportPartContentConverter(), new DBServerTypeSupportingConverter() }
            });
        }

        /// <summary>
        /// Adds an item to the cache using the specified key
        /// </summary>
        /// <param name="key">The key</param>
        /// <param name="value">The value</param>
        public void Add<T>(string key, T value)
        {
            RedisCache.Set(key, value);
        }

        /// <summary>
        /// Adds an item to the cache using the specified key and sets an expiration
        /// </summary>
        /// <param name="key">The key</param>
        /// <param name="value">The value</param>
        /// <param name="expiration">The expiration</param>
        public void AddWithExactLifetime(string key, object value, TimeSpan expiration)
        {
            RedisCache.SetWithLifetime(key, value, expiration);
        }

        /// <summary>
        /// Adds an item to the cache using the specified key and sets a sliding expiration
        /// </summary>
        /// <param name="key">The key</param>
        /// <param name="value">The value</param>
        /// <param name="expiration">The expiration</param>
        public void AddWithSlidingLifetime(string key, object value, TimeSpan expiration)
        {
            AddWithExactLifetime(key, value, expiration);
        }

        /// <summary>
        /// Checks if the cache contains the given key
        /// </summary>
        /// <param name="key">The key</param>
        /// <returns>Whether the cache contains the key</returns>
        public bool Contains(string key)
        {
            return RedisCache.Contains(key);
        }

        /// <summary>
        /// Retrieves the specified key from the cache
        /// </summary>
        /// <typeparam name="T">The object type</typeparam>
        /// <param name="key">The key</param>
        /// <returns>The value</returns>
        public T Get<T>(string key)
        {
            return RedisCache.Get<T>(key);
        }

        /// <summary>
        /// Removes the specified item from the cache.
        /// </summary>
        /// <param name="key">The key</param>
        public void Remove(string key)
        {
            RedisCache.Remove(key);
        }

        /// <summary>
        /// Removes the keys matching the specified pattern.
        /// </summary>
        /// <param name="pattern">The pattern. </param>
        public void RemoveKeyWithPattern(string pattern)
        {
            RedisCache.RemoveWithPattern(pattern);
        }

        /// <summary>
        /// Retrieves the specified key from the cache. If no value exists, a cache entry is created. 
        /// </summary>
        /// <typeparam name="T">The object type</typeparam>
        /// <param name="key">The key</param>
        /// <param name="executor">The function call that returns the data.</param>
        /// <returns></returns>
        public T Ensure<T>(string key, Func<T> executor)
        {
            return EnsureCache(executor, key, TimeSpan.Zero, (cacheKey, result, expiration) =>
            {
                Add(cacheKey, result);
            });
        }

        /// <summary>
        /// Retrieves the specified key from the cache. If no value exists, a cache entry is created. 
        /// </summary>
        /// <typeparam name="T">The type to convert the object to.</typeparam>
        /// <typeparam name="T">The object type</typeparam>
        /// <param name="expiration"> The expiration </param>
        /// <param name="executor">The function call that returns the data.</param>
        public T EnsureWithExactLifetime<T>(string key, TimeSpan expiration, Func<T> executor)
        {
            return EnsureCache(executor, key, expiration, (cacheKey, result, expirationTime) =>
            {
                AddWithExactLifetime(cacheKey, result, expirationTime);
            });
        }

        /// <summary>
        /// Retrieves the specified key from the cache. If no value exists, a cache entry is created. 
        /// </summary>
        /// <typeparam name="T">The object type</typeparam>
        /// <param name="expiration"> The sliding expiration </param>
        /// <param name="executor">The function call that returns the data.</param>
        public T EnsureWithSlidingLifetime<T>(string key, TimeSpan expiration, Func<T> executor)
        {
            return EnsureWithExactLifetime<T>(key, expiration, executor);
        }

        /// <summary>
        /// Update the cache with the specified value, if the cache does not exist, it is created.
        /// </summary>
        /// <typeparam name="T">The object type</typeparam>
        /// <param name="key">The key.</param>
        /// <param name="expiration">The expiration timeout as a timespan. This is a sliding value.</param>
        /// <param name="executor">The function call that returns the data.</param>
        public T UpdateWithSlidingLifetime<T>(string key, TimeSpan expiration, Func<T> executor)
        {
            var newValue = executor();

            if (newValue != null)
            {
                RedisCache.SetWithLifetime(key, newValue, expiration);
            }

            return newValue;
        }

        /// <summary>
        /// Retrieves the specified key from the cache. If no value exists, a cache entry is created. 
        /// </summary>
        /// <typeparam name="T">The type to convert the object to.</typeparam>
        /// <param name="executor">The function call that returns the data.</param>
        /// <param name="key">The key.</param>
        /// <param name="expiration">The expiration timeout as a timespan.</param>
        private T EnsureCache<T>(Func<T> executor, string key, TimeSpan expiration, Action<string, T, TimeSpan> addItemToCache)
        {
            var result = Get<T>(key);

            if (EqualityComparer<T>.Default.Equals(result, default))
            {
                result = Get<T>(key);

                if (EqualityComparer<T>.Default.Equals(result, default))
                {
                    var newValue = executor();

                    result = newValue;
                }

                if (result != null)
                {
                    addItemToCache(key, result, expiration);
                }
            }

            return result;
        }

        /// <summary>
        /// Dispose object
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Dispose object
        /// </summary>
        /// <param name="disposing"></param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
            }

            _disposed = true;
        }

        /// <summary>
        /// Dispose object
        /// </summary>
        ~RedisCacheProvider()
        {
            Dispose(false);
        }
    }
}
